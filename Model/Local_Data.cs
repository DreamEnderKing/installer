using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Platform;
using Newtonsoft.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace installer.Model
{
    class Local_Data
    {
        public string ConfigPath;      // 标记路径记录文件THUAI6.json的路径
        public Dictionary<string, string> Config;
        public string FilePath = ""; // 最后一级为THUAI6文件夹所在目录
        public bool Found = false;
        public Local_Data(string path)
        {
            ConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                "THUAI6.json");
            if (File.Exists(ConfigPath))
            {
                ReadConfig();
                if (Config.ContainsKey("InstallPath"))
                {
                    Found = true;
                    FilePath = Config["InstallPath"].Replace('\\', '/');
                }
            }
            else
            {
                Config = new Dictionary<string, string>
                {
                    { "THUAI6", "2023" }
                };
                SaveConfig();
            }
        }

        ~Local_Data()
        {
            SaveConfig();
        }

        public void ResetFilepath(string newPath)
        {
            if(Directory.Exists(Path.GetDirectoryName(newPath)))
            {
                Found = true;
                FilePath = newPath.Replace('\\', '/');
                if (Config.ContainsKey("InstallPath"))
                    Config["InstallPath"] = FilePath;
                else
                    Config.Add("InstallPath", FilePath);
                SaveConfig();
            }
        }
        public static bool IsUserFile(string filename)
        {
            if (filename.Substring(filename.Length - 3, 3).Equals(".sh") || filename.Substring(filename.Length - 4, 4).Equals(".cmd"))
                return true;
            if (filename.Equals("AI.cpp") || filename.Equals("AI.py"))
                return true;
            return false;
        }

        public void Change_all_hash(string topDir, Dictionary<string, string> jsonDict)  // 更改HASH
        {
            DirectoryInfo theFolder = new DirectoryInfo(@topDir);
            bool ifexist = false;

            // 遍历文件
            foreach (FileInfo NextFile in theFolder.GetFiles())
            {
                string filepath = topDir + @"/" + NextFile.Name;  // 文件路径
                                                                  //Console.WriteLine(filepath);
                foreach (KeyValuePair<string, string> pair in jsonDict)
                {
                    if (System.IO.Path.Equals(filepath, System.IO.Path.Combine(FilePath, pair.Key).Replace('\\', '/')))
                    {
                        ifexist = true;
                        string MD5 = Helper.GetFileMd5Hash(filepath);
                        jsonDict[pair.Key] = MD5;
                    }
                }
                if (!ifexist && NextFile.Name != "hash.json")
                {
                    string MD5 = Helper.GetFileMd5Hash(filepath);
                    string relapath = filepath.Replace(FilePath + '/', string.Empty);
                    jsonDict.Add(relapath, MD5);
                }
                ifexist = false;
            }

            // 遍历文件夹
            foreach (DirectoryInfo NextFolder in theFolder.GetDirectories())
            {
                if (System.IO.Path.Equals(NextFolder.FullName, System.IO.Path.GetFullPath(System.IO.Path.Combine(FilePath, playerFolder))))
                {
                    foreach (FileInfo NextFile in NextFolder.GetFiles())
                    {
                        if (NextFile.Name == "AI.cpp" || NextFile.Name == "AI.py")
                        {
                            string MD5 = Helper.GetFileMd5Hash(NextFile.FullName);
                            string relapath = NextFile.FullName.Replace('\\', '/').Replace(FilePath + '/', string.Empty);
                            jsonDict.Add(relapath, MD5);
                        }
                    }
                    continue;  // 如果是选手文件夹就忽略
                }
                Change_all_hash(NextFolder.FullName.Replace('\\', '/'), jsonDict);
            }
        }

        public void ReadConfig()
        {
            using (StreamReader r = new StreamReader(ConfigPath))
            {
                string json = r.ReadToEnd();
                if (json == null || json == "")
                {
                    json += @"{""THUAI6""" + ":" + @"""2023""}";
                }
                Config = Helper.TryDeserializeJson<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
        }

        public void SaveConfig()
        {
            using FileStream fs = new FileStream(ConfigPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            using StreamWriter sw = new StreamWriter(fs);
            fs.SetLength(0);
            sw.Write(JsonConvert.SerializeObject(Config));
            sw.Flush();
        }
    }
}
