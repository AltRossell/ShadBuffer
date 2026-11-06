using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ShadBuffer
{
    // what setup changed so remove can put it all back
    public sealed class State
    {
        public bool ProfileWritten { get; set; }
        public string ObsExe { get; set; }
        public string BackupDir { get; set; }
        public string EncoderBrand { get; set; }
        public bool SupportsHevc { get; set; }
        public bool SupportsAv1 { get; set; }
        public Dictionary<string, string> UserIni { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> Registry { get; set; } = new Dictionary<string, string>();

        public static State Load(Context ctx)
        {
            if (!File.Exists(ctx.StatePath))
            {
                return new State();
            }
            return new JavaScriptSerializer().Deserialize<State>(File.ReadAllText(ctx.StatePath));
        }

        public void Save(Context ctx)
        {
            Directory.CreateDirectory(ctx.AppDir);
            File.WriteAllText(ctx.StatePath, new JavaScriptSerializer().Serialize(this));
        }
    }
}
