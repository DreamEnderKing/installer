using COSXML;
using COSXML.Auth;
using COSXML.CosException;
using COSXML.Model.Object;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.GZip;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Collections.Concurrent;
using COSXML.Common;

namespace installer.Model
{
    public class Tencent_Cos
    {
        public string Appid { get; init; }      // 设置腾讯云账户的账户标识（APPID）
        public string Region { get; init; }     // 设置一个默认的存储桶地域
        public string BucketName { get; set; }
        public ConcurrentStack<Exception> Exceptions { get; set; }

        private string secretId = "***"; //"云 API 密钥 SecretId";
        private string secretKey = "***"; //"云 API 密钥 SecretKey";
        protected CosXmlServer cosXml;

        public Tencent_Cos(string appid, string region, string bucketName)
        {
            Appid = appid; Region = region; BucketName = bucketName;
            Exceptions = new ConcurrentStack<Exception>();
            // 初始化CosXmlConfig（提供配置SDK接口）
            var config = new CosXmlConfig.Builder()
                        .IsHttps(true)      // 设置默认 HTTPS 请求
                        .SetAppid(Appid)    // 设置腾讯云账户的账户标识 APPID
                        .SetRegion(Region)  // 设置一个默认的存储桶地域
                        .SetDebugLog(true)  // 显示日志
                        .Build();           // 创建 CosXmlConfig 对象
            long durationSecond = 1000;  // 每次请求签名有效时长，单位为秒
            QCloudCredentialProvider cosCredentialProvider = new DefaultQCloudCredentialProvider(secretId, secretKey, durationSecond);
            // 初始化 CosXmlServer
            cosXml = new CosXmlServer(config, cosCredentialProvider);
        }

        public async Task DownloadFileAsync(string savePath, string remotePath = null)
        {
            // download_dir标记根文件夹路径，key为相对根文件夹的路径（不带./）
            // 创建存储桶
            try
            {
                string bucket = $"{BucketName}-{Appid}";                                // 格式：BucketName-APPID
                string localDir = System.IO.Path.GetDirectoryName(savePath)     // 本地文件夹
                    ?? throw new Exception("本地文件夹路径获取失败");
                string localFileName = System.IO.Path.GetFileName(savePath);    // 指定本地保存的文件名
                GetObjectRequest request = new GetObjectRequest(bucket, remotePath ?? localFileName, localDir, localFileName);

                Dictionary<string, string> test = request.GetRequestHeaders();
                request.SetCosProgressCallback(delegate (long completed, long total)
                {
                    //Console.WriteLine(String.Format("progress = {0:##.##}%", completed * 100.0 / total));
                });
                // 执行请求
                GetObjectResult result = cosXml.GetObject(request);
                // 请求成功
            }
            catch (CosClientException clientEx)
            {
                Exceptions.Push(clientEx);
                throw clientEx;
            }
            catch (CosServerException serverEx)
            {
                Exceptions.Push(serverEx);
                throw serverEx;
            }
            catch (Exception ex)
            {
                Exceptions.Push(ex);
                throw ex;
                //MessageBox.Show($"下载{download_dir}时出现未知问题，请反馈");
            }
        }

        public async Task DownloadQueueAsync(ConcurrentQueue<string> queue, ConcurrentQueue<string> downloadFailed)
        {
            ThreadPool.SetMaxThreads(20, 20);
            for(int i = 0; i < queue.Count; i++)
            {
                string item;
                queue.TryDequeue(out item);
                ThreadPool.QueueUserWorkItem(async _ =>
                {
                    try
                    {
                        await DownloadFileAsync(item);
                    }
                    catch (Exception ex)
                    {
                        downloadFailed.Enqueue(item);
                    }
                });
            }
        }

        public void ArchieveUnzip(string zipPath, string targetDir)
        {
            Stream? inStream = null;
            Stream? gzipStream = null;
            TarArchive? tarArchive = null;
            try
            {
                using (inStream = File.OpenRead(zipPath))
                {
                    using (gzipStream = new GZipInputStream(inStream))
                    {
                        tarArchive = TarArchive.CreateInputTarArchive(gzipStream);
                        tarArchive.ExtractContents(targetDir);
                        tarArchive.Close();
                    }
                }
            }
            catch
            {
                //出错
            }
            finally
            {
                if (tarArchive != null) tarArchive.Close();
                if (gzipStream != null) gzipStream.Close();
                if (inStream != null) inStream.Close();
            }
        }

        public static void UpdateHash()
        {
            while (true)
            {
                if (Directory.Exists(Data.FilePath))
                {
                    string json;
                    if (!File.Exists(System.IO.Path.Combine(Data.FilePath, "hash.json")))
                    {
                        Console.WriteLine("hash.json文件丢失！即将重新下载该文件！");
                        GetNewHash();
                    }
                    using (StreamReader r = new StreamReader(System.IO.Path.Combine(Data.FilePath, "hash.json")))
                        json = r.ReadToEnd();
                    json = json.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("/", @"\\");
                    Dictionary<string, string> jsonDict = Utils.DeserializeJson1<Dictionary<string, string>>(json);
                    Change_all_hash(Data.FilePath, jsonDict);
                    OverwriteHash(jsonDict);
                    break;
                }
                else
                {
                    Console.WriteLine("读取路径失败！请重新输入文件路径：");
                    Data.ResetFilepath(Console.ReadLine() ?? "");
                }
            }
        }

        public static int DeleteAll()
        {
            DirectoryInfo di = new DirectoryInfo(Data.FilePath + "/THUAI6");
            //DirectoryInfo player = new DirectoryInfo(System.IO.Path.GetFullPath(System.IO.Path.Combine(Data.FilePath, playerFolder)));
            FileInfo[] allfile = di.GetFiles();
            try
            {
                foreach (FileInfo file in allfile)
                {
                    //if(file.Name == "AI.cpp" || file.Name == "AI.py")
                    //{
                    //    string filename = System.IO.Path.GetFileName(file.FullName);
                    //    file.MoveTo(System.IO.Path.Combine(Data.FilePath, filename));
                    //    continue;
                    //}
                    file.Delete();
                }
                FileInfo userFileCpp = new FileInfo(Data.FilePath + "/THUAI6/win/CAPI/cpp/API/src/AI.cpp");
                FileInfo userFilePy = new FileInfo(Data.FilePath + "/THUAI6/win/CAPI/python/PyAPI/AI.py");
                userFileCpp.MoveTo(System.IO.Path.Combine(Data.FilePath + "/THUAI6", System.IO.Path.GetFileName(userFileCpp.FullName)));
                userFilePy.MoveTo(System.IO.Path.Combine(Data.FilePath + "/THUAI6", System.IO.Path.GetFileName(userFilePy.FullName)));
                foreach (DirectoryInfo subdi in di.GetDirectories())
                {
                    subdi.Delete(true);
                }
                FileInfo hashFile = new FileInfo(Data.FilePath + "/hash.json");
                hashFile.Delete();
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("权限不足，无法删除！");
                return -2;
            }
            catch (DirectoryNotFoundException)
            {
                Console.WriteLine("文件夹没有找到，请检查是否已经手动更改路径");
                return -3;
            }
            catch (IOException)
            {
                Console.WriteLine("文件已经打开，请关闭后再删除");
                return -1;
            }

            string json2;
            Dictionary<string, string>? dict;
            string existpath = System.IO.Path.Combine(Data.dataPath, "THUAI6.json");
            using FileStream fs = new FileStream(existpath, FileMode.Open, FileAccess.ReadWrite);
            using (StreamReader r = new StreamReader(fs))
            {
                json2 = r.ReadToEnd();
                if (json2 == null || json2 == "")
                {
                    json2 += @"{""THUAI6""" + ":" + @"""2023""}";
                }
                dict = Utils.TryDeserializeJson<Dictionary<string, string>>(json2);
                if (dict == null || !dict.ContainsKey("download"))
                {
                    dict?.Add("download", "false");
                }
                else
                {
                    dict["download"] = "false";
                }
            }
            using FileStream fs2 = new FileStream(existpath, FileMode.Open, FileAccess.ReadWrite);
            using StreamWriter sw = new StreamWriter(fs2);
            fs2.SetLength(0);
            sw.Write(JsonConvert.SerializeObject(dict));
            sw.Close();
            fs2.Close();
            try
            {
                File.Delete(Data.path);
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("权限不足，无法删除！");
                return -2;
            }
            catch (DirectoryNotFoundException)
            {
                Console.WriteLine("文件夹没有找到，请检查是否已经手动更改路径");
                return -3;
            }
            catch (IOException)
            {
                Console.WriteLine("文件已经打开，请关闭后再删除");
                return -1;
            }
            return 0;
        }

        public static void OverwriteHash(Dictionary<string, string> jsonDict)
        {
            string Contentjson = JsonConvert.SerializeObject(jsonDict);
            Contentjson = Contentjson.Replace("\r", String.Empty).Replace("\n", String.Empty).Replace(@"\\", "/");
            File.WriteAllText(@System.IO.Path.Combine(Data.FilePath, "hash.json"), Contentjson);
        }

        public static int MoveProgram(string newPath)
        {
            DirectoryInfo newdi = new DirectoryInfo(newPath + "/THUAI6");
            DirectoryInfo olddi = new DirectoryInfo(Data.FilePath + "/THUAI6");
            try
            {
                if (!Directory.Exists(newPath + "/THUAI6"))
                    Directory.CreateDirectory(newPath + "/THUAI6");
                foreach (DirectoryInfo direct in olddi.GetDirectories())
                {
                    direct.MoveTo(System.IO.Path.Combine(newPath + "/THUAI6", direct.Name));
                }
                foreach (FileInfo file in olddi.GetFiles())
                {
                    file.MoveTo(System.IO.Path.Combine(newPath + "/THUAI6", file.Name));
                }
                olddi.Delete();
            }
            catch (DirectoryNotFoundException)
            {
                Console.WriteLine("原路径未找到！请检查文件是否损坏");
                if (newdi.GetDirectories().Length != 0)
                {
                    foreach (DirectoryInfo newdirect in newdi.GetDirectories())
                    {
                        newdirect.MoveTo(System.IO.Path.Combine(Data.FilePath + "/THUAI6", newdirect.Name));
                    }
                }
                if (newdi.GetFiles().Length != 0)
                {
                    foreach (FileInfo file in newdi.GetFiles())
                    {
                        file.MoveTo(System.IO.Path.Combine(Data.FilePath + "/THUAI6", file.Name));
                    }
                }
                Console.WriteLine("移动失败！");
                if (newdi.Exists)
                    newdi.Delete();
                return -2;
            }
            catch (IOException)
            {
                Console.WriteLine("文件已打开或者目标路径下有同名文件！");
                if (newdi.GetDirectories().Length != 0)
                {
                    foreach (DirectoryInfo newdirect in newdi.GetDirectories())
                    {
                        newdirect.MoveTo(System.IO.Path.Combine(Data.FilePath + "/THUAI6", newdirect.Name));
                    }
                }
                if (newdi.GetFiles().Length != 0)
                {
                    foreach (FileInfo file in newdi.GetFiles())
                    {
                        file.MoveTo(System.IO.Path.Combine(Data.FilePath + "/THUAI6", file.Name));
                    }
                }
                if (newdi.Exists)
                    newdi.Delete();
                Console.WriteLine("移动失败！");
                return -1;
            }
            FileInfo hashFile = new FileInfo(Data.FilePath + "/hash.json");
            hashFile.MoveTo(newPath + "/hash.json");
            Data.ResetFilepath(newPath);
            Console.WriteLine("更改路径成功!");
            return 0;
        }
        public static async Task main(string[] args)
        {
            var client = new HttpClient();
            var web = new WebConnect.Web();
            Data date = new Data("");
            while (true)
            {
                Console.WriteLine($"1. 更新hash.json   2. 检查更新   3.下载{ProgramName}  4.删除{ProgramName}  5.启动进程  6.移动{ProgramName}到其它路径");
                string choose = Console.ReadLine() ?? "";
                if (choose == "1")
                {
                    if (!CheckAlreadyDownload())
                    {
                        Console.WriteLine($"未下载{ProgramName}，请先执行下载操作！");
                        continue;
                    }
                    UpdateHash();
                }
                else if (choose == "2")
                {
                    if (!CheckAlreadyDownload())
                    {
                        Console.WriteLine($"未下载{ProgramName}，请先执行下载操作！");
                        continue;
                    }
                    while (true)
                    {
                        if (Data.FilePath != null && Directory.Exists(Data.FilePath))
                        {
                            Check(SettingsModel.UsingOS.Win);
                            break;
                        }
                        else
                        {
                            Console.WriteLine("读取路径失败！请重新输入文件路径：");
                            Data.ResetFilepath(Console.ReadLine() ?? "");
                        }
                    }
                }
                else if (choose == "3")
                {
                    if (CheckAlreadyDownload())
                    {
                        Console.WriteLine($"已经将{ProgramName}下载到{Data.FilePath}！若要重新下载请先完成删除操作！");
                    }
                    else
                    {
                        string newpath;
                        Console.WriteLine("请输入下载路径：");
                        newpath = Console.ReadLine() ?? "";
                        Data.ResetFilepath(newpath);
                        DownloadAll();
                    }
                }
                else if (choose == "4")
                {
                    DeleteAll();
                }
                else if (choose == "5")
                {
                    if (CheckAlreadyDownload())
                    {
                        Process.Start(System.IO.Path.Combine(Data.FilePath, startName));
                    }
                    else
                    {
                        Console.WriteLine($"未下载{ProgramName}，请先执行下载操作！");
                    }
                }
                else if (choose == "6")
                {
                    string newPath;
                    newPath = Console.ReadLine() ?? "";
                    MoveProgram(newPath);
                }
                else if (choose == "7")
                {
                    Console.WriteLine("请输入email：");
                    string username = Console.ReadLine() ?? "";
                    Console.WriteLine("请输入密码：");
                    string password = Console.ReadLine() ?? "";

                    await web.LoginToEEsast(client, username, password);
                }
                else if (choose == "8")
                {
                    await web.UserDetails(client);
                }
                else if (choose == "9")
                {
                    await web.UploadFiles(client, "", "", "");
                }
                else if (choose == "exit")
                {
                    return;
                }
            }
        }

        public static int CheckSelfVersion()
        {
            string keyHead = "Installer/";
            Tencent_cos_download downloader = new Tencent_cos_download();
            string hashName = "installerHash.json";
            string dir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName)
                ?? throw new Exception("Failed to get current directory");
            int result = 0;
            try
            {
                if (File.Exists(System.IO.Path.Combine(dir, hashName)))
                    File.Delete(System.IO.Path.Combine(dir, hashName));
                downloader.download(System.IO.Path.Combine(dir, hashName), hashName);
            }
            catch
            {
                return -1;
            }
            string json;
            using (StreamReader r = new StreamReader(System.IO.Path.Combine(dir, hashName)))
                json = r.ReadToEnd();
            json = json.Replace("\r", string.Empty).Replace("\n", string.Empty);
            var jsonDict = Utils.TryDeserializeJson<Dictionary<string, string>>(json);
            string md5 = "";
            List<string> awaitUpdate = new List<string>();
            if (jsonDict != null)
            {
                foreach (KeyValuePair<string, string> pair in jsonDict)
                {
                    md5 = GetFileMd5Hash(System.IO.Path.Combine(dir, pair.Key));
                    if (md5.Length == 0)  // 文档不存在
                    {
                        downloader.download(System.IO.Path.Combine(dir, pair.Key), keyHead + pair.Key);
                    }
                    else if (md5.Equals("conflict"))
                    {
                        //MessageBox.Show($"检查{pair.Key}更新时遇到问题，请反馈", "读取出错", //MessageBoxButton.OK, //MessageBoxImage.Error);
                    }
                    else if (md5 != pair.Value)  // MD5不匹配
                    {
                        if (pair.Key.Substring(0, 12).Equals("InstallerUpd"))
                        {
                            File.Delete(System.IO.Path.Combine(dir, pair.Key));
                            downloader.download(System.IO.Path.Combine(dir, pair.Key), keyHead + pair.Key);
                        }
                        else
                        {
                            result = 1;
                            awaitUpdate = awaitUpdate.Append(pair.Key).ToList();
                        }
                    }
                }
            }
            else
                return -1;
            string Contentjson = JsonConvert.SerializeObject(awaitUpdate);
            Contentjson = Contentjson.Replace("\r", String.Empty).Replace("\n", String.Empty).Replace(@"\\", "/");
            File.WriteAllText(@System.IO.Path.Combine(dir, "updateList.json"), Contentjson);
            return result;
        }

        static public bool SelfUpdateDismissed()
        {
            string json;
            string dir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName)
                ?? throw new Exception("Failed to get directory!");
            if (!File.Exists(System.IO.Path.Combine(dir, "updateList.json")))
                return false;
            using (StreamReader r = new StreamReader(System.IO.Path.Combine(dir, "updateList.json")))
                json = r.ReadToEnd();
            json = json.Replace("\r", string.Empty).Replace("\n", string.Empty);
            List<string>? jsonList;
            if (json != null)
                jsonList = Utils.TryDeserializeJson<List<string>>(json);
            else
                return false;
            if (jsonList != null && jsonList.Contains("Dismiss"))
            {
                listJsonClear(System.IO.Path.Combine(dir, "updateList.json"));
                return true;
            }
            return false;
        }

        static private void listJsonClear(string directory)
        {
            List<string> list = new List<string>();
            list.Add("None");
            StreamWriter sw = new StreamWriter(directory, false);
            sw.WriteLine(JsonConvert.SerializeObject(list));
            sw.Close();
        }
    }

}
