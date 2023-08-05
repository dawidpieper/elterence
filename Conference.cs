using System;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Net.Security;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Reflection;
using System.Numerics;
using Newtonsoft.Json;
using CS_AES_CTR;
using Un4seen.Bass;
using Un4seen.Bass.AddOn.Mix;
using Concentus;
using Concentus.Common.CPlusPlus;
using Concentus.Enums;
using Concentus.Structs;

namespace Elterence {

public class ConnectionParametersChangedEventArgs : EventArgs {
public dynamic Parameters {get;set;}
public ConnectionParametersChangedEventArgs(dynamic parameters) : base() {Parameters = parameters;}
}

public class ChannelChangedEventArgs : EventArgs {
public Channel CurrentChannel {get;set;}
public ChannelChangedEventArgs(Channel currentChannel) : base() {CurrentChannel = currentChannel;}
}

public class PacketReceivedEventArgs : EventArgs {
public int Type {get;set;}
public ConferenceUser User {get;set;}
public int P1 {get;set;}
public int P2 {get;set;}
public int P3 {get;set;}
public int P4 {get;set;}
public byte[] Message {get;set;}
}

public class ChatMessageEventArgs : EventArgs {
public ConferenceUser User {get;set;}
public string Message {get;set;}
}

public enum MessageType: int {
Audio=1,
Text=2,
Whisper=3,
EncryptedWhisper=4,
Reemit=201,
Ping=251,
Pong=252
}

public class Conference {

public event EventHandler<ConnectionParametersChangedEventArgs> ConnectionParametersChanged;
public event EventHandler<ChannelChangedEventArgs> ChannelChanged;
public event EventHandler<PacketReceivedEventArgs> PacketReceived;

public event EventHandler<ChatMessageEventArgs> ChatMessage;
public bool Connected {get; private set;}
public float Latency {get; private set;}
public int UID {get; private set;}
//streams to be defined

//bool _Reconnecting;
string _LastUUID;
string _LastPassword;
RSA _Key;
TcpClient _TCP;
SslStream _SSL;
UdpClient _UDP;
CancellationTokenSource _UDPCancellation;
CancellationTokenSource _TCPCancellation;
byte[] _Secret;
int _CHID;
int _Stamp;
int _Index;
Dictionary<int,byte[]> _ChannelSecrets;
Dictionary<int, Dictionary<int, List<int>>> _Received;

string _Username;
string _Token;
Channel _CurrentChannel;

Mutex _TCPMutex;
Thread _TCPThread;
bool _TCPRequested;
Mutex _CMDMutex;

Position _Position;
int _Mixer;
int _FrameID;
Mutex _RecordMutex;
int _Record;
RECORDPROC _RecordProc;
float[] _RecordBuffer;
Dictionary<int, Transmitter> _Transmitters;
OpusEncoder _Encoder;

private static bool _BASSLoaded=false;

public Conference() {

if(!_BASSLoaded) {

string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
if(!File.Exists(path+@"\win32/bass.dll")) path=Environment.CurrentDirectory;
bool suc=false;
if(IntPtr.Size == 8) {
suc=Bass.LoadMe(path+@"\win64");
if(suc) suc=BassMix.LoadMe(path+@"\win64");
} else {
suc=Bass.LoadMe(path+@"\win32");
if(suc) suc=BassMix.LoadMe(path+@"\win32");
}

Bass.BASS_Init(-1, 48000, 0, IntPtr.Zero);
Bass.BASS_RecordInit(-1);

_BASSLoaded=true;
}

//_Reconnecting=false;
_LastUUID=null;
_LastPassword=null;
Latency=0;
_Key = RSA.Create(2048);
_TCP=null;
_SSL=null;
_UDP=null;
UID=0;
_Secret=null;
_CHID=0;
_Stamp=0;
_Index=0;
Connected=false;
_ChannelSecrets = new Dictionary<int, byte[]>();
_Username=null;
_Token=null;
_TCPThread=null;
_TCPMutex = new Mutex();
_CMDMutex = new Mutex();
_TCPRequested=false;
_Received = new Dictionary<int, Dictionary<int, List<int>>>();
_CurrentChannel = new Channel();

_Position = new Position();
_Mixer = BassMix.BASS_Mixer_StreamCreate(48000, 2, BASSFlag.BASS_MIXER_RESUME);
_Encoder = null;
_FrameID = 0;
_RecordBuffer = null;
_RecordMutex = new Mutex();
_RecordProc = new RECORDPROC(RecordProc);
int flags = (int)BASSFlag.BASS_SAMPLE_FLOAT;
int period=5;
flags = (int)(((ushort)flags) | (uint)(period << 16));
_Record = Bass.BASS_RecordStart(48000, 2, (BASSFlag)flags, _RecordProc, IntPtr.Zero);
Bass.BASS_ChannelPlay(_Mixer, false);

_Transmitters = new Dictionary<int, Transmitter>();
}

~Conference() {
Bass.BASS_StreamFree(_Mixer);
}

public async Task<bool> ConnectAsync(string username, string token) {
_Username = username;
_Token = token;
// _mutes = new string[] { username };
// bigpackets = false;

try {
_TCP = new TcpClient("conferencing.elten.link", 8133);
_SSL = new SslStream(_TCP.GetStream(), false, (sender, certificate, chain, sslPolicyErrors) => true);
_SSL.AuthenticateAsClient("conferencing.elten.link");
_SSL.ReadTimeout = 2000;
_SSL.WriteTimeout = 2000;

var resp = Command("login", new { login = username, token = token, publickey = Convert.ToBase64String(_Key.ExportRSAPublicKey()) });
if (resp != null) {
UID = resp["id"];
string secr64 = resp["secret"];
_Secret = Convert.FromBase64String(secr64);
_TCPCancellation?.Cancel();

_TCPCancellation = new CancellationTokenSource();

_TCPThread = new Thread(() => {
var c = _TCPCancellation;
while(!c.IsCancellationRequested) {
if(!_TCPRequested)
Thread.Sleep(500);
else
Thread.Sleep(100);

Update();
}
});
_TCPThread.Start();

await ConnectUDPAsync();
Connected = true;
}
else {
return false;
}

return true;
}
catch (Exception) {return false;
}
}

public async Task ConnectUDPAsync() {
_UDPCancellation?.Cancel();

_UDP = new UdpClient();
IPEndPoint endPoint = new IPEndPoint(Dns.GetHostAddresses("conferencing.elten.link")[0], 8133);
_UDP.Connect(endPoint);

await _UDP.SendAsync(_Secret, _Secret.Length);

_UDPCancellation = new CancellationTokenSource();
var t = Task.Run(async () => {
while (true) {
if (_UDPCancellation.IsCancellationRequested)
break;
UdpReceiveResult result = await _UDP.ReceiveAsync(_UDPCancellation.Token);
Receive(result.Buffer);
}
});
}

(int userid, int stamp, int index, int type) Extract(byte[] data) {
int userid=data[0] + data[1] * 256;;
int stamp = data[2] + data[3] * 256 + data[4] * (int)Math.Pow(256, 2);
int index = data[5] + data[6] * 256;
int type = data[7];
return (userid, stamp, index, type);
}

public ConferenceUser GetUser(int uid) {
foreach(var user in _CurrentChannel.Users)
if(user.UID == uid) return user;
ConferenceUser u = new ConferenceUser();
u.UID = uid;
return u;
}

bool Receive(byte[] data) {
try {
if(data.Length<16) return false;
(int userid, int stamp, int index, int type) = Extract(data);
if(type<200 && _CHID==0) return false;
if(!_Received.ContainsKey(userid))
_Received.Add(userid, new Dictionary<int, List<int>>());
if(!_Received[userid].ContainsKey(type))
_Received[userid].Add(type, new List<int>());
if(type<200 && (userid!=0 && _Received[userid][type].Contains(index))) return false;
if(userid!=0) _Received[userid][type].Add(index);
byte[] message;
int crc = data[12] + data[13] * 256 + data[14] * (int)Math.Pow(256, 2) + data[15] * (int)Math.Pow(256, 3);
(int p1, int p2, int p3, int p4) = (data[8], data[9], data[10], data[11]);
message = new byte[0];
if(data.Length>16) {
if(!_ChannelSecrets.ContainsKey(stamp)) return false;
byte[] key = _ChannelSecrets[stamp];
byte[] iv = new byte[16];
byte[] m = new byte[data.Length-16];
Array.Copy(data, 0, iv, 0, 16);
Array.Copy(data, 16, m, 0, data.Length - 16);
AES_CTR ctr = new AES_CTR(key, iv);
message = new byte[m.Length];
ctr.DecryptBytes(message, m);
}
if(type<200) {
var crcb = Crc32.Hash(message);
int real_crc = crcb[0] + crcb[1]*256 + crcb[2]*256*256 + crcb[3]*256*256*256;
if(real_crc==crc || crc==0) {
var e = new PacketReceivedEventArgs();
e.Type=type;
ConferenceUser user = GetUser(userid);
e.User=user;
e.P1=p1;
e.P2=p2;
e.P3=p3;
e.P4=p4;
e.Message=message;
PacketReceived?.Invoke(this, e);
switch(type) {
case (int)MessageType.Audio:
if(_Transmitters.ContainsKey(user.UID)) {
var transmitter = _Transmitters[user.UID];
transmitter.Put(message, type, p1, p2, p3*256+p4, index);
}
break;
case (int)MessageType.Text:
var ec = new ChatMessageEventArgs();
ec.User=user;
ec.Message = Encoding.UTF8.GetString(message);
ChatMessage?.Invoke(this, ec);
break;
case (int)MessageType.Whisper:
if(_Transmitters.ContainsKey(user.UID)) {
var transmitter = _Transmitters[user.UID];
transmitter.Put(message, type, p1, p2, p3*256+p4, index);
}
break;
case (int)MessageType.EncryptedWhisper:
try {
if(_Transmitters.ContainsKey(user.UID)) {
byte[] enchead = new byte[256];
Array.Copy(message, 0, enchead, 0, 256);
byte[] head = _Key.Decrypt(enchead, RSAEncryptionPadding.Pkcs1);
int keysize = head[0];
byte[] key = new byte[keysize/8];
Array.Copy(head, 1, key, 0, keysize/8);
byte[] iv = new byte[16];
Array.Copy(head, keysize/8+1, iv, 0, 16);
byte[] frg1 = new byte[245 - (1+keysize/8+16)];
Array.Copy(head, 245-frg1.Length, frg1, 0, frg1.Length);
byte[] frg2 = null;
byte[] result;
if(message.Length>256) {
using (Aes aes = Aes.Create()) {
aes.Padding = PaddingMode.PKCS7;
aes.Mode = CipherMode.CBC;
aes.Key = key;
aes.IV = iv;
byte[] rest = new byte[message.Length-256];
Array.Copy(message, 256, rest, 0, rest.Length);
frg2 = aes.DecryptCbc(rest, iv, PaddingMode.PKCS7);
}
}
int size = frg1.Length;
if(frg2!=null) size+=frg2.Length;
result = new byte[size];
Array.Copy(frg1, 0, result, 0, frg1.Length);
if(frg2!=null)
Array.Copy(frg2, 0, result, frg1.Length, frg2.Length);
var transmitter = _Transmitters[user.UID];
transmitter.Put(result, (int)MessageType.Whisper, p1, p2, p3*256+p4, index);
}
} catch(Exception) {}
break;
}
}
} else {
switch(type) {
case (int)MessageType.Ping:
Pong(message);
break;
}



}
return true;
} catch(Exception) {
return false;
}
}

byte[] GeneratePacket(int type, byte[] message, int p1=0, int p2=0, int p3=0, int p4=0, int uid=-1, int index=-1, bool ignore_stamp=false) {
if(_CHID==0 && type<200) return null;
if((message==null || message.Length==0) && type<10)
return null;
if(!_ChannelSecrets.ContainsKey(_Stamp) && type<200)
return null;
var crcb = Crc32.Hash(message);
if(index==-1) index = _Index++;
if(uid==-1) uid=UID;
byte[] iv = new byte[16];
iv[0]=(byte)(uid%256);
iv[1]=(byte)(uid/256);
iv[2]=(byte)(_Stamp%256);
iv[3]=(byte)((_Stamp/256)%256);
iv[4]=(byte)(_Stamp/256/256);
iv[5]=(byte)(index%256);
iv[6]=(byte)(index/256);
iv[7]=(byte)(type);
iv[8]=(byte)(p1);
iv[9]=(byte)(p2);
iv[10]=(byte)(p3);
iv[11]=(byte)(p4);
iv[12]=crcb[0];
iv[13]=crcb[1];
iv[14]=crcb[2];
iv[15]=crcb[3];
byte[] m;
if(message==null || message.Length==0) m = new byte[0];
else {
if(ignore_stamp || _Stamp==0 || !_ChannelSecrets.ContainsKey(_Stamp))
m=message;
else {
byte[] key = _ChannelSecrets[_Stamp];
AES_CTR ctr = new AES_CTR(key, iv);
m = new byte[message.Length];
ctr.EncryptBytes(m, message);
}
}
byte[] data = new byte[16+m.Length];
Array.Copy(iv, 0, data, 0, 16);
Array.Copy(m, 0, data, 16, m.Length);
return data;
}

void Send(int type, byte[] message, int p1=0, int p2=0, int p3=0, int p4=0, int uid=-1, int index=-1, bool ignore_stamp=false) {
var data = GeneratePacket(type, message, p1, p2, p3, p4, uid, index, ignore_stamp);
SendPacket(data);
}

async void SendPacket(byte[] data) {
await _UDP.SendAsync(data);
}

void Pong(byte[] message) {
int t = (int)((DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond) % 1000000);
Send((int)MessageType.Pong, message, t%256, t/256);
}

public bool ExecuteCommand(string cmd, object parameters = null) {
if(_TCP == null)
return false;

var json = new Dictionary<string, object>();
json[":command"]=cmd;

if(parameters!=null)
foreach (var property in parameters.GetType().GetProperties())
json[property.Name] = property.GetValue(parameters);

string txt = Newtonsoft.Json.JsonConvert.SerializeObject(json);

lock(_TCPMutex) {
try {
if (txt.Length > 64) {
byte[] compressed = CompressString(txt);
string h = "d" + ToBase36(compressed.Length) + "\n";
byte[] hbuf = Encoding.Default.GetBytes(h);
byte[] buffer = new byte[compressed.Length + hbuf.Length];
hbuf.CopyTo(buffer, 0);
compressed.CopyTo(buffer, hbuf.Length);
var r = WriteToTcpStreamAsync(buffer).Result;
} else {
string s = txt + "\n";
var r = WriteToTcpStreamAsync(s).Result;
}
}
catch (Exception) {
return true;
}
}

return false;
}

private async Task<bool> WriteToTcpStreamAsync(string data) {
byte[] buffer = Encoding.Default.GetBytes(data);
return await WriteToTcpStreamAsync(buffer);
}

private async Task<bool> WriteToTcpStreamAsync(byte[] buffer) {
await _SSL.WriteAsync(buffer, 0, buffer.Length);
await _TCP.GetStream().FlushAsync();
//_SendBytes += buffer.Length;
return true;
}

private const string Base36Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

private static long FromBase36(string value) {
string Base36Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
if (string.IsNullOrWhiteSpace(value)) return 0;
value = value.ToUpper();
bool negative = false;
if(value[0] == '-') {
negative = true;
value = value.Substring(1, value.Length - 1);
}
var decoded = 0L;
            for (var i = 0; i < value.Length; ++i)
decoded += Base36Digits.IndexOf(value[i]) * (long)BigInteger.Pow(Base36Digits.Length, value.Length - i - 1);
return negative ? decoded * -1 : decoded;
        }

private static string ToBase36(long value) {
bool negative = value < 0;
value = Math.Abs(value);
string encoded = string.Empty;
do
encoded = Base36Digits[(int)(value % Base36Digits.Length)] + encoded;
while ((value /= Base36Digits.Length) != 0);
return negative ? "-" + encoded : encoded;
        }

private static byte[] CompressString(string input) {
byte[] inputBytes = Encoding.Default.GetBytes(input);

byte[] output;

using (MemoryStream outputStream = new MemoryStream()) {
using (Stream zlibStream = new ZLibStream(outputStream, CompressionMode.Compress)) {
zlibStream.Write(inputBytes, 0, inputBytes.Length);
}

output = outputStream.ToArray();

}
return output;
}

public dynamic Command(string cmd, object parameters = null) {
bool rec = false;
string ans = null;

lock (_CMDMutex) {
ExecuteCommand(cmd, parameters);
try {
ans = ReadLineFromTcpStream();
//_ReceivedBytes += ans.Length;
}
catch (Exception) {
rec = true;
}
}

if (!rec) {
if (ans != null && ans[0] == 'd') {
string ns = ans;
int size = (int)FromBase36(ns.Substring(1));
byte[] a = ReadBytesFromTcpStreamAsync(size).Result;
//_ReceivedBytes += size;
ans = DecompressString(a);
}


dynamic json = Newtonsoft.Json.JsonConvert.DeserializeObject(ans);

if (json is Newtonsoft.Json.Linq.JObject) {
if (json["status"] != "success")
return null;
}

return json;
} else {
//if(!reconnecting) {
//reconnectThread = new Thread(Reconnect);
//}
return null;
}
}

public async Task<dynamic> CommandAsync(string cmd, object parameters = null) {
return await Task.Run(() => Command(cmd, parameters));
}

private string ReadLineFromTcpStream() {
StringBuilder sb = new StringBuilder();
int character;

while ((character = _SSL.ReadByte()) != -1) {
char c = (char)character;
if (c == '\n')
break;
sb.Append(c);
}
return sb.ToString();
}

private async Task<byte[]> ReadBytesFromTcpStreamAsync(int count) {
byte[] buffer = new byte[count];
int bytesRead = 0;

while (bytesRead < count) {
int read = await _SSL.ReadAsync(buffer, bytesRead, count - bytesRead);
if (read == 0)
throw new IOException("Unexpected end of stream.");

bytesRead += read;
}

return buffer;
}

private static string DecompressString(byte[] input) {
using (MemoryStream inputStream = new MemoryStream(input)) {
using (MemoryStream outputStream = new MemoryStream()) {
using (Stream zlibStream = new ZLibStream(inputStream, CompressionMode.Decompress)) {
zlibStream.CopyTo(outputStream);
}
return Encoding.Default.GetString(outputStream.ToArray());
}
}
}

private void Reconnect()
{
// Implement reconnection logic here
}

public void Update() {
dynamic resp = Command("update");

if (resp is Newtonsoft.Json.Linq.JObject && resp["updated"]==true) {
_CHID = resp.channel;
_Stamp = resp.channel_stamp;
_LastUUID = resp.channel_uuid;
_Index = 1;

if(resp.channel_secret != null) {
string secr64 = resp.channel_secret;
_ChannelSecrets[_Stamp] = Convert.FromBase64String(secr64);
}
if(resp["params"] != null) {
ConnectionParametersChanged?.Invoke(this, new ConnectionParametersChangedEventArgs(resp["params"]));
if(resp["params"]["channel"] != null) {
Channel channel = JsonConvert.DeserializeObject<Channel>(resp["params"]["channel"].ToString());
if(_CurrentChannel.ID != channel.ID) {
_Position.X = channel.Width/2;
_Position.Y = channel.Height/2;
_Position.Dir = 0;
}
var userids = channel.Users.Select(p => p.UID);
var transmitterids = _Transmitters.Select(p => p.Key);
var toRemove = new List<int>();
foreach((int uid, Transmitter transmitter) in _Transmitters) {
if(!userids.Contains(uid)) toRemove.Add(uid);
else {
transmitter.Update(channel.Channels, channel.Framesize, channel.Spatialization);
}
}
foreach(var t in toRemove) _Transmitters.Remove(t);
foreach(ConferenceUser user in channel.Users) {
if(!transmitterids.Contains(user.UID)) {
var transmitter = new Transmitter(channel.Channels, channel.Framesize, channel.Spatialization, _Position, user);
transmitter.SetMixer(_Mixer, _Mixer);
_Transmitters.Add(user.UID, transmitter);
}
}
lock(_RecordMutex) {
var app = OpusApplication.OPUS_APPLICATION_VOIP;
if(channel.CodecApplication == CODECAPPLICATION.Audio) app = OpusApplication.OPUS_APPLICATION_AUDIO;
_Encoder = OpusEncoder.Create(48000, channel.Channels, app);
_Encoder.Bitrate = channel.Bitrate*1000;
}
_CurrentChannel = channel;
ChannelChanged?.Invoke(null, new ChannelChangedEventArgs(channel));
}
}
}
}

public async Task<Channel[]> ListChannels() {
var c = await CommandAsync("list");
if(c==null) return null;
List<Channel> channels = new List<Channel>();
foreach(var ch in c.channels) {
Channel channel = JsonConvert.DeserializeObject<Channel>(ch.ToString());
channels.Add(channel);
}
return channels.ToArray();
}

public bool JoinChannel(int id, string password=null, string uuid=null) {
var r = Command("join", new {channel=id, password=password, uuid=uuid});
Update();
bool st = r!=null && r["status"]=="success";
if(st)
_LastPassword = password;
return st;
}

public bool LeaveChannel() {
var r = Command("leave");
Update();
bool st = r!=null && r["status"]=="success";
return st;
}

public bool RecordProc(int handle, IntPtr buffer, int length, IntPtr user) {
lock(_RecordMutex) {
int previousLength = 0;
if(_RecordBuffer!=null) previousLength = _RecordBuffer.Count();
float[] buf = new float[previousLength + length/4];
if(_RecordBuffer!=null) Array.Copy(_RecordBuffer, 0, buf, 0, previousLength);
Marshal.Copy(buffer, buf, previousLength, length/4);

Decimal framesize = _CurrentChannel.Framesize;
int channels = _CurrentChannel.Channels;
int frameSamples = (int)(48*channels*framesize);
int frames = buf.Count() / frameSamples;

byte[] opusFrame = new byte[1280];
for(int i=0; i<frames; ++i) {
if(_Encoder!=null) {
int bytes = _Encoder.Encode(buf, i*frameSamples, (int)(48*framesize), opusFrame, 0, opusFrame.Count());
byte[] frame = new byte[bytes];
Array.Copy(opusFrame, 0, frame, 0, bytes);
if(_FrameID>60000) _FrameID=0;
++_FrameID;
Send((int)MessageType.Audio, frame, _Position.X, _Position.Y, _FrameID/256, _FrameID%256);

}
}

int leftSamples = buf.Count() % frameSamples;

_RecordBuffer = new float[leftSamples];
Array.Copy(buf, buf.Count() - leftSamples, _RecordBuffer, 0, leftSamples);
}
return true;
}

public void Chat(string message) {
Send((int)MessageType.Text, Encoding.UTF8.GetBytes(message));
}
}
}