using System;
using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Elterence {

public class ConferenceUser {
[JsonProperty("id", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(0)]
public int UID=0;

[JsonProperty("name", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue("")]
public string Name="";

[JsonProperty("supervisor", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(null)]
public int? Supervisor=null;

[JsonProperty("speech_requested", DefaultValueHandling = DefaultValueHandling.Populate)]
[DefaultValue(false)]
public bool SpeechRequested=false;
}
}