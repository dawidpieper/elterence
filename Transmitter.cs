using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;
using Concentus;
using Concentus.Common.CPlusPlus;
using Concentus.Enums;
using Concentus.Structs;
using Un4seen.Bass;
using Un4seen.Bass.AddOn.Mix;

namespace Elterence {

public class Transmitter {

OpusDecoder _Decoder;
int _Stream, _Whisper;
int _ListenerX, _ListenerY, _ListenerDir, _TransmitterX, _TransmitterY;
public ConferenceUser User {get; private set;}
int _Channels;
Decimal _Framesize;
Position _Position;
Mutex _Mutex;

Dictionary<int, (byte[], int, int, int, int, int)> _Queue;
int _LastIndex;
int _LastFrameID;
STREAMPROC _StreamProc, _WhisperProc;

bool _Freed;

public Transmitter(int channels, Decimal framesize, SPATIALIZATION spatialization, Position position, ConferenceUser user) {
_Channels = channels;
_Framesize = framesize;
//spatialization
_Position = position;
User = user;

_ListenerX = _Position.X;
_ListenerY = _Position.Y;
_ListenerDir = _Position.Dir;
_TransmitterX = -1;
_TransmitterY = -1;

_Decoder = OpusDecoder.Create(48000, _Channels);
_Freed = false;

_Mutex = new Mutex();

_LastIndex = 0;
_LastFrameID=0;
_Queue = new Dictionary<int, (byte[], int, int, int, int, int)>();

_StreamProc = new STREAMPROC(StreamProc);
_Stream = Bass.BASS_StreamCreate(48000, _Channels, BASSFlag.BASS_STREAM_DECODE|BASSFlag.BASS_SAMPLE_FLOAT, _StreamProc, IntPtr.Zero);
_WhisperProc = new STREAMPROC(StreamProc);
_Whisper = Bass.BASS_StreamCreate(48000, _Channels, BASSFlag.BASS_STREAM_DECODE|BASSFlag.BASS_SAMPLE_FLOAT, _WhisperProc, IntPtr.Zero);
}

public void Update(int channels, Decimal framesize, SPATIALIZATION spatialization) {
lock(_Mutex) {
_Channels = channels;
_Framesize = framesize;
//spatialization
Bass.BASS_StreamFree(_Stream);
Bass.BASS_StreamFree(_Whisper);
_Decoder = OpusDecoder.Create(48000, _Channels);
_Queue = new Dictionary<int, (byte[], int, int, int, int, int)>();
_StreamProc = new STREAMPROC(StreamProc);
_Stream = Bass.BASS_StreamCreate(48000, _Channels, BASSFlag.BASS_STREAM_DECODE|BASSFlag.BASS_SAMPLE_FLOAT, _StreamProc, IntPtr.Zero);
_WhisperProc = new STREAMPROC(StreamProc);
_Whisper = Bass.BASS_StreamCreate(48000, _Channels, BASSFlag.BASS_STREAM_DECODE|BASSFlag.BASS_SAMPLE_FLOAT, _WhisperProc, IntPtr.Zero);
}
}

public void Put(byte[] frame, int type=1, int x=-1, int y=-1, int frame_id=0, int index=-1) {
lock(_Mutex) {
int n=index;
if(n<100) n+=65535;
if(_LastIndex<index || index<100) {
(byte[], int, int, int, int, int) val = (frame, type, x, y, index, frame_id);
if(!_Queue.ContainsKey(n)) _Queue.Add(n, val);
else _Queue[n]=val;
_LastIndex=index;
}
}
}

public void Move(int x, int y) {
if(x<=0 && y<=0) return;
_TransmitterX=x;
_TransmitterY=y;
UpdatePosition();
}

public void UpdatePosition() {
_ListenerX = _Position.X;
_ListenerY = _Position.Y;
_ListenerDir = _Position.Dir;
if(_TransmitterX<=0 || _TransmitterY<=0 || _ListenerX<=0 || _ListenerY<=0) return;
double rx = (_TransmitterX-_ListenerX)/8.0;
double ry = (_TransmitterY-_ListenerY)/8.0;
if(_ListenerDir!=0) {
double sn = Math.Sin(Math.PI/180*-_ListenerDir);
double cs = Math.Cos(Math.PI/180*-_ListenerDir);
rx = rx * cs - ry * sn;
ry = rx * sn + ry * cs;
}
float pos=(float)rx;
if(pos<-1) pos=-1;
if(pos>1) pos=1;
float vol = (float)(1-Math.Sqrt(Math.Pow(Math.Abs(ry)*0.5,2)+Math.Pow(Math.Abs(rx)*0.5, 2)));
if(vol<0) vol=0;
if(_Freed) {
Bass.BASS_ChannelSetAttribute(_Stream, BASSAttribute.BASS_ATTRIB_PAN, pos);
Bass.BASS_ChannelSetAttribute(_Whisper, BASSAttribute.BASS_ATTRIB_PAN, pos);
Bass.BASS_ChannelSetAttribute(_Stream, BASSAttribute.BASS_ATTRIB_VOL, vol);
Bass.BASS_ChannelSetAttribute(_Whisper, BASSAttribute.BASS_ATTRIB_VOL, vol);
}
}

public int StreamProc(int handle, IntPtr buffer, int length, IntPtr user) {
if(_Freed) return 0;
try {
bool whisper = (handle==_Whisper);
lock(_Mutex) {
List<float[]> buf = new List<float[]>();
int total=0;
if(buf.Count==1) total=buf[0].Count();
int messageType = (int)MessageType.Audio;
while((_Queue.Count*_Framesize - length/4/48) > 150) {
int? keyOrNull = _Queue.Where(pair => pair.Value.Item2==messageType).OrderBy(pair => pair.Key).Select(pair => (int?)pair.Key).FirstOrDefault();
if(keyOrNull==null) break;
int key = (int)keyOrNull;
_Queue.Remove(key);
}
while(_Queue.Count>0) {
if(whisper) messageType = (int)MessageType.Whisper;
int? keyOrNull = _Queue.Where(pair => pair.Value.Item2==messageType).OrderBy(pair => pair.Key).Select(pair => (int?)pair.Key).FirstOrDefault();
if(keyOrNull==null) break;
int key = (int)keyOrNull;
(byte[] frame, int type, int x, int y, int index, int frame_id) val;
_Queue.Remove(key, out val);
(byte[] frame, int type, int x, int y, int index, int frame_id) = val;
float[] pcm;
int fid=frame_id;
if(fid>0 && fid<100 && _LastFrameID>59000) fid+=60000;
int lostFrames=0;
if(_LastFrameID!=0 && frame_id!=0) {
lostFrames = fid-_LastFrameID-1;
int lf = lostFrames;
if(lf>3) lf=3;
for(int i=0; i<lf; ++i) {
pcm = new float[(int)(_Framesize*48*_Channels)];
if(i<lf-1)
_Decoder.Decode(null, 0, 0, pcm, 0, (int)(48*_Framesize), false);
else
_Decoder.Decode(frame, 0, frame.Count(), pcm, 0, (int)(48*_Framesize), true);
buf.Add(pcm);
total+=pcm.Count();
}
}
_LastFrameID = frame_id;
if(x>0 && y>0) Move(x,y);
pcm = new float[(int)(_Framesize*48*_Channels)];
_Decoder.Decode(frame, 0, frame.Count(), pcm, 0, (int)(48*_Framesize), false);
buf.Add(pcm);
total+=pcm.Count();
}
if(buf.Count==0) return 0;
float[] audio = buf.SelectMany(x => x).ToArray();
int len = length/4;
if(total<len) len=total;
Marshal.Copy(audio, total-len, buffer, len);
return len*4;
}
} catch(Exception) {return 0;}
}

public void SetMixer(int streamMixer, int whisperMixer) {
if(!_Freed) {
lock(_Mutex) {
BassMix.BASS_Mixer_StreamAddChannel(streamMixer, _Stream, 0);
BassMix.BASS_Mixer_StreamAddChannel(whisperMixer, _Whisper, 0);
}
}
}

public void Free() {
lock(_Mutex) {
Bass.BASS_StreamFree(_Stream);
Bass.BASS_StreamFree(_Whisper);
_Freed = true;
}
}

~Transmitter() {Free();}


}
}