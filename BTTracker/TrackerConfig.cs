using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;

namespace BTTracker
{
    internal class TrackerConfig
    {
        internal TimeSpan AnnounceInterval { get; set; }
        internal WorkingModes WorkingMode { get; set; }
        internal NetworkModes NetworkMode { get; set; }
        internal List<IPEndPoint> Endpoints { get; set; }=new List<IPEndPoint>();
        internal string ConnectionString { get; set; }


        private TrackerConfig()
        {

        }

        internal static TrackerConfig FromFile(string filepath)
        {
            FileInfo fileInfo = new FileInfo(filepath);
            if (!fileInfo.Exists) filepath = Path.Combine(Directory.GetCurrentDirectory(), filepath);
            if (!fileInfo.Exists) throw new Exception(string.Format("{0} not found.",filepath));
            var doc = Toml.Parse(File.ReadAllText(filepath));
            var table = doc.ToModel();
            var general = table["general"] as TomlTable;
            if (general == null) throw new Exception("Configuration file broken.");
            var config = new TrackerConfig();
            config.AnnounceInterval = TimeSpan.FromSeconds((long)general["AnnounceInterval"]);
            string rawworkingmode = (string)general["WorkingMode"];
            switch (rawworkingmode)
            {
                case "static":
                    config.WorkingMode = WorkingModes.Static;
                    break;
                case "dynamic":
                default:
                    config.WorkingMode = WorkingModes.Dynamic;
                    break;
            }
            config.ConnectionString = (string)general["MySqlConnectionString"];

            var network = table["network"] as TomlTable;
            if (network == null) throw new Exception("Configuration file broken.");
            config.NetworkMode = Enum.Parse<NetworkModes>((string)network["NetworkMode"],true);
            if (network.ContainsKey("IPv4Address")&&network.ContainsKey("IPv4Port"))
            {
                config.Endpoints.Add(new IPEndPoint(IPAddress.Parse((string)network["IPv4Address"]), Convert.ToInt16((long)network["IPv4Port"])));
            }
            if (network.ContainsKey("IPv6Address") && network.ContainsKey("IPv6Port"))
            {
                config.Endpoints.Add(new IPEndPoint(IPAddress.Parse((string)network["IPv6Address"]), Convert.ToInt16((long)network["IPv6Port"])));
            }
            return config;
        }


        internal static TrackerConfig Default=>new TrackerConfig() { 
            AnnounceInterval=TimeSpan.FromMinutes(30),
            NetworkMode=NetworkModes.Mixed,
            WorkingMode=WorkingModes.Dynamic,
            Endpoints=new List<IPEndPoint>() {new IPEndPoint(IPAddress.Any,6969), new IPEndPoint(IPAddress.IPv6Any,6969) }
        };

        internal enum NetworkModes
        {
            LAN, Public, Mixed
        }

        internal enum WorkingModes
        {
            Static, Dynamic
        }
    }
}
