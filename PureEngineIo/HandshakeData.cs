﻿using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using Utf8Json;

namespace PureEngineIo
{
    public class HandshakeData
    {
        [DataMember(Name = "sid")]
        public string Sid;

        [DataMember(Name = "upgrades")]
        public IList<string> Upgrades = ImmutableList<string>.Empty;

        [DataMember(Name = "pingInterval")]
        public long PingInterval;

        [DataMember(Name = "pingTimeout")]
        public long PingTimeout;

        internal static HandshakeData FromString(string data) => JsonSerializer.Deserialize<HandshakeData>(data);
    }
}
