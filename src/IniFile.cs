using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ShadOBS
{
    // edits single keys and leaves every other line of the file alone
    public sealed class IniFile
    {
        private readonly string path;
        private readonly List<string> lines;

        public IniFile(string path)
        {
            this.path = path;
            lines = File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8).ToList() : new List<string>();
        }

        public string Get(string section, string key)
        {
            int index = FindKey(section, key);
            return index < 0 ? null : lines[index].Substring(lines[index].IndexOf('=') + 1);
        }

        public void Set(string section, string key, string value)
        {
            int index = FindKey(section, key);
            if (index >= 0)
            {
                lines[index] = key + "=" + value;
                return;
            }

            int header = FindSection(section);
            if (header < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0)
                {
                    lines.Add("");
                }
                lines.Add("[" + section + "]");
                lines.Add(key + "=" + value);
                return;
            }
            lines.Insert(header + 1, key + "=" + value);
        }

        public void Remove(string section, string key)
        {
            int index = FindKey(section, key);
            if (index >= 0)
            {
                lines.RemoveAt(index);
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(true));
        }

        private int FindSection(string section)
        {
            string header = "[" + section + "]";
            return lines.FindIndex(line => line.Trim().Equals(header, StringComparison.OrdinalIgnoreCase));
        }

        private int FindKey(string section, string key)
        {
            int header = FindSection(section);
            if (header < 0)
            {
                return -1;
            }
            for (int i = header + 1; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("["))
                {
                    return -1;
                }
                int equals = line.IndexOf('=');
                if (equals > 0 && line.Substring(0, equals).Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
