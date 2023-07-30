using System;
using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Elterence {

public enum VBRTYPE {
None=0,
VBR=1,
CVBR=2
}

public enum CODECAPPLICATION {
Voip=0,
Audio=1
}

public enum SPATIALIZATION {
Panning=0,
HRTF=1,
RoundTable=2
}

public class Channel {
[JsonProperty("id", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(0)]
public int ID=0;

[JsonProperty("name", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue("")]
public string Name="";

[JsonProperty("bitrate", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(80)]
public int Bitrate=80;

[JsonProperty("framesize", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(40)]
public Decimal Framesize=40;

[JsonProperty("vbr_type", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(VBRTYPE.VBR)]
public VBRTYPE VBRType = VBRTYPE.VBR;

[JsonProperty("codec_application", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(CODECAPPLICATION.Voip)]
public CODECAPPLICATION CodecApplication = CODECAPPLICATION.Voip;

[JsonProperty("prediction_disabled", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(false)]
public bool PredictionDisabled = false;

[JsonProperty("fec", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(false)]
public bool FEC = true;

[JsonProperty("public", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(false)]
public bool Public = false;

[JsonProperty("users", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(null)]
public ConferenceUser[] Users = new ConferenceUser[0];

[JsonProperty("passworded", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(false)]
public bool Passworded = false;

[JsonProperty("spatialization", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(SPATIALIZATION.Panning)]
public SPATIALIZATION Spatialization = SPATIALIZATION.Panning;

[JsonProperty("channels", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(2)]
 public int Channels = 2;

[JsonProperty("lang", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue("")]
public string Lang = "";

[JsonProperty("creator", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue("")]
public string Creator = "";

[JsonProperty("width", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(15)]
public int Width = 15;

[JsonProperty("height", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(15)]
public int Height = 15;

//:objects
//:administrators

[JsonProperty("key_len", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(256)]
public int KeyLen = 256;

[JsonProperty("group_id", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(0)]
public int GroupID = 0;

[JsonProperty("waiting_type", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(0)]
public int WaitingType = 0;

//:banned

[JsonProperty("permanent", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(false)]
public bool Permanent = false;

[JsonProperty("password", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue("")]
public string Password = "";

[JsonProperty("uuid", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue("")]
public string UUID = "";

[JsonProperty("motd", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue("")]
public string MOTD = "";

[JsonProperty("allow_guests", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue("")]
public bool AllowGuests = false;

[JsonProperty("room_id", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(0)]
public int? RoomID = 0;

[JsonProperty("followed", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(false)]
public bool Followed = false;

[JsonProperty("join_url", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue("")]
public string JoinURL = "";

[JsonProperty("conference_mode", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(0)]
public int ConferenceMode = 0;

//:whitelist

[JsonProperty("followers_count", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(0)]
public int FollowersCount = 0;

[JsonProperty("stream_bitrate", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(0)]
public int StreamBitrate = 96;

[JsonProperty("stream_framesize", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(0)]
public Decimal StreamFramesize = 60;

}
}