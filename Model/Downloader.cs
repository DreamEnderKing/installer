using COSXML.CosException;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vision;

namespace installer.Model
{
    public struct UpdateInfo                                         // 更新信息，包括新版本版本号、更改文件数和新文件数
    {
        public string status;
        public int changedFileCount;
        public int newFileCount;
    }

    public class Downloader
    {
        #region 属性区
        public class UserInfo
        {
            public string _id = "";
            public string email = "";
        }
        public string ProgramName = "THUAI6";                     // 要运行或下载的程序名称
        public string StartName = "maintest.exe";          // 启动的程序名
        private Local_Data Data;
        private Tencent_Cos Cloud;

        private HttpClient Client = new HttpClient();
        private EEsast Web = new EEsast();


        ConcurrentQueue<string> downloadFile = new ConcurrentQueue<string>(); // 需要下载的文件名
        ConcurrentQueue<string> downloadFailed = new ConcurrentQueue<string>();  //更新失败的文件名
        public List<string> UpdateFailed
        {
            get { return downloadFailed.ToList(); }
        }
        public bool UpdatePlanned
        {
            get; set;
        }

        public void ResetDownloadFailedInfo()
        {
            downloadFailed.Clear();
        }

        private int filenum = 0;                                   // 总文件个数

        public string Route { get; set; }
        public string Username { get; set; } = String.Empty;
        public string Password { get; set; } = String.Empty;
        public string CodeRoute { get; set; } = String.Empty;
        public string? Language { get; set; } = null;
        public string PlayerNum { get; set; } = "nSelect";
        public enum LaunchLanguage { cpp, python };
        public LaunchLanguage launchLanguage { get; set; } = LaunchLanguage.cpp;
        public enum UsingOS { Win, Linux, OSX };
        public UsingOS usingOS { get; set; }
        public class Updater
        {
            public string Message;
            public enum Status { newUser, menu, move, working, initializing, disconnected, error, successful, login, web, launch };
            public Status UpdateStatus;
            public bool Working { get; set; }
            public bool CombatCompleted { get => false; }
            public bool UploadReady { get; set; } = false;
            public bool ProfileAvailable { get; set; }
        }
        public bool LoginFailed { get; set; } = false;
        public bool RememberMe { get; set; }

        #endregion

        #region 方法区
        public Downloader()
        {
            Data = new Local_Data("");
            Route = Data.FilePath;
            Cloud = new Tencent_Cos("1314234950", "ap-beijing", "thuai6");
            usingOS = ReadUsingOS();
        }

        /// <summary>
        /// 全新安装
        /// </summary>
        public void Install()
        {
            string json1 = "hash.json", content1;

            try
            {
                // 如果json存在就删了重新下
                if (File.Exists(Path.Combine(Data.FilePath, json1)))
                {
                    File.Delete(Path.Combine(Data.FilePath, json1));
                }
                Cloud.DownloadFileAsync(Path.Combine(Data.FilePath, json1), json1).Wait();
            }
            catch (CosClientException clientEx)
            {
                // 请求失败
                Console.WriteLine("CosClientException: " + clientEx.ToString() + Environment.NewLine);
                return;
            }
            catch (CosServerException serverEx)
            {
                // 请求失败
                Console.WriteLine("CosClientException: " + serverEx.ToString() + Environment.NewLine);
                return;
            }
            using (StreamReader r = new StreamReader(System.IO.Path.Combine(Data.FilePath, json1)))
                content1 = r.ReadToEnd();
            content1 = content1.Replace("\r", string.Empty).Replace("\n", string.Empty);

            string zp = System.IO.Path.Combine(Data.FilePath, "THUAI6.tar.gz");
            Cloud.DownloadFileAsync(zp, "THUAI6.tar.gz").Wait();
            Cloud.ArchieveUnzip(zp, Data.FilePath);
            File.Delete(zp);

            string json2 = "THUAI6.json", content2;
            Dictionary<string, string>? dict;
            using (FileStream file = new FileStream(System.IO.Path.Combine(Data.DataPath, json2), FileMode.Open, FileAccess.ReadWrite))
            {
                using (StreamReader r = new StreamReader(file))
                    content2 = r.ReadToEnd();
                if (json2 == null || json2 == "")
                {
                    json2 += @"{""THUAI6""" + ":" + @"""2023""}";
                }
                dict = Helper.TryDeserializeJson<Dictionary<string, string>>(json2);
                if (dict == null || !dict.ContainsKey("download"))
                {
                    dict?.Add("download", "true");
                }
                else
                {
                    dict["download"] = "true";
                }
                file.SetLength(0);


                using (StreamWriter sw = new StreamWriter(file))
                    sw.Write(JsonConvert.SerializeObject(dict));
            }

            ScanFiles();
            Cloud.DownloadQueueAsync(downloadFile, downloadFailed).Wait();
        }

        /// <summary>
        /// save settings
        /// </summary>
        /// TO DO: 将这一部分从model移动（可能拆分）到ViewModel以避开对话框
        public bool Install()
        {
            if (CheckAlreadyDownload())
            {
                //MessageBoxResult repeatOption = //MessageBox.Show($"文件已存在于{Downloader.Program.Data.FilePath},是否移动到新位置？", "重复安装", //MessageBoxButton.YesNo, //MessageBoxImage.Warning, //MessageBoxResult.No);
                // ask if abort install, with warning sign, defalut move instead of abort;
                if (true)//repeatOption == MessageBoxResult.No)
                {
                    Route = Data.FilePath;
                    return false;
                }
                else
                {
                    MoveProgram(Route);
                    return true;
                }
            }
            else
            {
                Data.ResetFilepath(Route);
                Cloud.DownloadAll();
                return true;
            }
        }
        public int Move()
        {
            int state = Cloud.MoveProgram(Route);
            if (state != 0)
                Route = Data.FilePath;
            return state;

        }

        /// <summary>
        /// 检查更新
        /// </summary>
        /// <returns></returns>
        public Updater.Status CheckUpdate()
        {
            UpdateInfo updateInfo = CheckOS(usingOS);
            if (updateInfo.newFileCount == -1)
            {
                if (updateInfo.changedFileCount == -1)
                {
                    return Updater.Status.error;
                }
                else
                {
                    return Updater.Status.disconnected;
                }
            }
            else
            {
                if (updateInfo.changedFileCount != 0 || updateInfo.newFileCount != 0)
                {
                    Updater.Message = $"{updateInfo.newFileCount}个新文件，{updateInfo.changedFileCount}个文件变化";
                }
                return Updater.Status.menu;
            }
        }

        public UpdateInfo ScanFiles()
        {
            string json, MD5, jsonName;
            int newFile = 0, updateFile = 0;
            newFileName.Clear();
            updateFileName.Clear();
            jsonName = "hash.json";
            UpdateInfo updateInfo;
            try
            {
                // 如果json存在就删了重新下
                if (File.Exists(System.IO.Path.Combine(Data.FilePath, jsonName)))
                {
                    File.Delete(System.IO.Path.Combine(Data.FilePath, jsonName));
                    Cloud.Download(System.IO.Path.Combine(Data.FilePath, jsonName), jsonName);
                }
                else
                {
                    Cloud.Download(System.IO.Path.Combine(Data.FilePath, jsonName), jsonName);
                }
            }
            catch (CosClientException clientEx)
            {
                // 请求失败
                updateInfo.status = "ClientEx: " + clientEx.ToString();
                updateInfo.newFileCount = -1;
                updateInfo.changedFileCount = 0;
                return updateInfo;
            }
            catch (CosServerException serverEx)
            {
                // 请求失败
                updateInfo.status = "ServerEx: " + serverEx.ToString();
                updateInfo.newFileCount = -1;
                updateInfo.changedFileCount = 0;
                return updateInfo;
            }

            using (StreamReader r = new StreamReader(System.IO.Path.Combine(Data.FilePath, jsonName)))
                json = r.ReadToEnd();
            json = json.Replace("\r", string.Empty).Replace("\n", string.Empty);
            var jsonDict = Helper.DeserializeJson1<Dictionary<string, string>>(json);
            string updatingFolder = "";
            switch (OS)
            {
                case UsingOS.Win:
                    updatingFolder = "THUAI6/win";
                    break;
                case UsingOS.Linux:
                    updatingFolder = "THUAI6/lin";
                    break;
                case UsingOS.OSX:
                    updatingFolder = "THUAI6/osx";
                    break;
            }
            foreach (KeyValuePair<string, string> pair in jsonDict)
            {
                if (pair.Key.Length > 10 && (pair.Key.Substring(0, 10).Equals(updatingFolder)) || pair.Key.Substring(pair.Key.Length - 4, 4).Equals(".pdf"))
                {
                    MD5 = Helper.GetFileMd5Hash(System.IO.Path.Combine(Data.FilePath, pair.Key.TrimStart(new char[] { '.', '/' })));
                    if (MD5.Length == 0)  // 文档不存在
                        newFileName.Enqueue(pair.Key);
                    else if (MD5.Equals("conflict"))
                    {
                        if (pair.Key.Equals("THUAI6/win/CAPI/cpp/.vs/CAPI/v17/Browse.VC.db"))
                        {
                            //MessageBox.Show($"visual studio未关闭：\n" +
                            //$"对于visual studio 2022，可以更新，更新会覆盖visual studio中已经打开的选手包；\n" +
                            //$"若使用其他版本的visual studio是继续更新出现问题，请汇报；\n" +
                            //$"若您自行修改了选手包，请注意备份；\n" +
                            //$"若关闭visual studio后仍弹出，请汇报。\n\n",
                            //"visual studio未关闭", //MessageBoxButton.OK, //MessageBoxImage.Information);
                        }
                        else;
                            //MessageBox.Show($"检查{pair.Key}更新时遇到问题，请反馈", "读取出错", //MessageBoxButton.OK, //MessageBoxImage.Error);
                    }
                    else if (!MD5.Equals(pair.Value) && !Local_Data.IsUserFile(System.IO.Path.GetFileName(pair.Key)))  // MD5不匹配
                        updateFileName.Enqueue(pair.Key);
                }
            }

            newFile = newFileName.Count;
            updateFile = updateFileName.Count;
            filenum = newFile + updateFile;
            //Console.WriteLine("----------------------" + Environment.NewLine);

            if (newFile + updateFile == 0)
            {
                updateInfo.status = "latest";
                updateInfo.newFileCount = 0;
                updateInfo.changedFileCount = 0;
                newFileName.Clear();
                updateFileName.Clear();
            }
            else
            {
                updateInfo.status = "old";
                //TODO:获取版本号
                updateInfo.newFileCount = newFile;
                /*
                foreach (string filename in newFileName)
                {
                    Console.WriteLine(filename);
                }
                */
                updateInfo.changedFileCount = updateFile;
                /*
                foreach (string filename in updateFileName)
                {
                    Console.WriteLine(filename);
                }
                Console.Write(Environment.NewLine + "是否下载新文件？ y/n：");
                if (Console.Read() != 'y')
                    Console.WriteLine("下载取消!");
                else
                    Download();
                */
                UpdatePlanned = true;
            }
            return updateInfo;
        }

        public bool Update()
        {
            if (UpdatePlanned)
            {
                int newFile = 0;
                int totalnew = newFileName.Count, totalupdate = updateFileName.Count;
                filenum = totalnew + totalupdate;
                updateFailed.Clear();
                if (newFileName.Count > 0 || updateFileName.Count > 0)
                {
                    try
                    {
                        int cnt = newFileName.Count;
                        if (cnt <= 20)
                        {
                            while (newFileName.TryDequeue(out var filename))
                            {
                                Cloud.Download(System.IO.Path.Combine(@Data.FilePath, filename), filename.TrimStart(new char[] { '.', '/' }));
                                //Console.WriteLine(filename + "下载完毕!" + Environment.NewLine);
                                Interlocked.Increment(ref newFile);
                            }
                        }
                        else
                        {
                            const int nthread = 8;
                            var thrds = new List<Thread>();
                            for (int i = 0; i < nthread; i++)
                            {
                                var thrd = new Thread(() =>
                                {
                                    while (newFileName.TryDequeue(out var filename))
                                    {
                                        Cloud.Download(System.IO.Path.Combine(@Data.FilePath, filename), filename.TrimStart(new char[] { '.', '/' }));
                                        //Console.WriteLine(filename + "下载完毕!" + Environment.NewLine);
                                        Interlocked.Increment(ref newFile);
                                    }
                                });
                                thrd.Start();
                                thrds.Add(thrd);
                            }
                            foreach (var thrd in thrds)
                            {
                                thrd.Join();
                            }
                        }
                        // 读取 Interlocked.CompareExchange(ref newFile, 0, 0);

                        int upcnt = updateFileName.Count;
                        if (upcnt <= 20)
                        {
                            while (updateFileName.TryDequeue(out var filename))
                            {
                                try
                                {
                                    File.Delete(System.IO.Path.Combine(@Data.FilePath, filename));
                                    Cloud.Download(System.IO.Path.Combine(@Data.FilePath, filename), filename.TrimStart(new char[] { '.', '/' }));
                                }
                                catch (System.IO.IOException)
                                {
                                    updateFailed = updateFailed.Append(filename).ToList();
                                }
                                catch
                                {
                                    if (filename.Substring(filename.Length - 4, 4).Equals(".pdf"))
                                    {
                                        //MessageBox.Show($"由于曾经发生过的访问冲突，下载器无法更新{filename}\n"
                                            //+ $"请手动删除{filename}，然后再试一次。");
                                    }
                                    else
                                        //MessageBox.Show($"更新{filename}时遇到未知问题，请反馈");
                                    updateFailed = updateFailed.Append(filename).ToList();
                                }
                                Interlocked.Increment(ref newFile);
                            }
                        }
                        else
                        {
                            const int nthread = 8;
                            var thrds = new List<Thread>();

                            for (int i = 0; i < nthread; i++)
                            {
                                var thrd = new Thread(() =>
                                {
                                    while (updateFileName.TryDequeue(out var filename))
                                    {
                                        try
                                        {
                                            File.Delete(System.IO.Path.Combine(@Data.FilePath, filename));
                                            Cloud.Download(System.IO.Path.Combine(@Data.FilePath, filename), filename.TrimStart(new char[] { '.', '/' }));
                                        }
                                        catch (System.IO.IOException)
                                        {
                                            updateFailed = updateFailed.Append(filename).ToList();
                                        }
                                        catch
                                        {
                                            if (filename.Substring(filename.Length - 4, 4).Equals(".pdf"))
                                            {
                                                //MessageBox.Show($"由于曾经发生过的访问冲突，下载器无法更新{filename}\n"
                                                    //+ $"请手动删除{filename}，然后再试一次。");
                                            }
                                            else
                                                //MessageBox.Show($"更新{filename}时遇到未知问题，请反馈");
                                            updateFailed = updateFailed.Append(filename).ToList();
                                        }
                                        Interlocked.Increment(ref newFile);
                                    }
                                });
                                thrd.Start();
                                thrds.Add(thrd);
                            }
                            foreach (var thrd in thrds)
                            {
                                thrd.Join();
                            }
                        }
                        if (updateFailed.Count == 0)
                            UpdatePlanned = false;
                    }
                    catch (CosClientException clientEx)
                    {
                        // 请求失败
                        //MessageBox.Show("连接错误:" + clientEx.ToString());
                        Console.WriteLine("CosClientException: " + clientEx.ToString() + Environment.NewLine);
                    }
                    catch (CosServerException serverEx)
                    {
                        // 请求失败
                        //MessageBox.Show("连接错误:" + serverEx.ToString());
                        Console.WriteLine("CosClientException: " + serverEx.ToString() + Environment.NewLine);
                    }
                    catch (Exception)
                    {
                        //MessageBox.Show("未知错误且无法定位到出错文件，请反馈");
                        throw;
                    }
                }
                else
                    Console.WriteLine("当前平台已是最新版本！" + Environment.NewLine);
                newFileName.Clear();
                updateFileName.Clear();

                if (updateFailed.Count == 0)
                    return true;
            }
            return false;
        }

        public bool CheckAlreadyDownload()  // 检查是否已经下载
        {
            string existpath = System.IO.Path.Combine(Data.DataPath, "THUAI6.json");
            if (!File.Exists(existpath))  // 文件不存在
            {
                using FileStream fs = new FileStream(existpath, FileMode.Create, FileAccess.ReadWrite);
                return false;
            }
            else  // 文件存在
            {
                using FileStream fs = new FileStream(existpath, FileMode.Open, FileAccess.Read);
                using StreamReader sr = new StreamReader(fs);
                string json = sr.ReadToEnd();
                if (json == null || json == "")
                {
                    json += @"{""THUAI6""" + ":" + @"""2023""}";
                }
                var dict = Helper.TryDeserializeJson<Dictionary<string, string>>(json);
                if (dict == null || !dict.ContainsKey("download") || "false" == dict["download"])
                {
                    return false;
                }
                else if (dict["download"] == "true")
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public async Task<int> Login()
        {
            return await Web.LoginToEEsast(Client, Username, Password);
        }

        public bool RememberUser()
        {
            int result = 0;
            result |= Web.WriteJson("email", Username);
            result |= Web.WriteJson("password", Password);
            return result == 0;
        }
        public bool RecallUser()
        {
            var username = Web.ReadJson("email");
            if (username == null || username.Equals(""))
            {
                Username = "";
                return false;
            }
            Username = username;

            var password = Web.ReadJson("password");
            if (password == null || password.Equals(""))
            {
                Password = "";
                return false;
            }
            Password = password;

            return true;
        }
        public bool ForgetUser()
        {
            int result = 0;
            result |= Web.WriteJson("email", "");
            result |= Web.WriteJson("password", "");
            return result == 0;
        }

        public bool Update()
        {
            try
            {
                return Cloud.Update();
            }
            catch
            {
                return false;
            }
        }
        public int Uninst()
        {
            return Cloud.DeleteAll();
        }

        public bool Launch()
        {
            if (Cloud.CheckAlreadyDownload())
            {
                //Process.Start(System.IO.Path.Combine(Data.FilePath, startName));
                switch (RunProgram.RunInfo.mode)
                {
                    case RunProgram.RunMode.ServerOnly:
                        RunProgram.StartServer(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                            RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        break;
                    case RunProgram.RunMode.ServerForDebugOnly:
                        RunProgram.StartServerForDebug(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                            RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        break;
                    case RunProgram.RunMode.GUIAttendGameOnly:
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID,
                            false, RunProgram.RunInfo.occupation, RunProgram.RunInfo.type);
                        break;
                    case RunProgram.RunMode.GUIVisit:
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, 0, true, 1, 1);
                        break;
                    case RunProgram.RunMode.GUIAndAICpp:
                        RunProgram.StartServerForDebug(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunCpp(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID,
                        false, RunProgram.RunInfo.occupation, RunProgram.RunInfo.type);
                        break;
                    case RunProgram.RunMode.GUIAndAIPython:
                        RunProgram.StartServerForDebug(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunPython(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID,
                        false, RunProgram.RunInfo.occupation, RunProgram.RunInfo.type);
                        break;
                    case RunProgram.RunMode.ServerAndCpp:
                        RunProgram.StartServer(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunCpp(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        break;
                    case RunProgram.RunMode.ServerAndPython:
                        RunProgram.StartServer(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunPython(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        break;
                    case RunProgram.RunMode.ServerAndCppVisit:
                        RunProgram.StartServer(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunCpp(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID, true, 0, 1);
                        break;
                    case RunProgram.RunMode.ServerAndPythonVisit:
                        RunProgram.StartServer(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunPython(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID, true, 0, 1);
                        break;
                    case RunProgram.RunMode.ServerDebugAndCppVisit:
                        RunProgram.StartServerForDebug(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunCpp(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID, true, 0, 1);
                        break;
                    case RunProgram.RunMode.ServerDebugAndPythonVisit:
                        RunProgram.StartServerForDebug(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunPython(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID, true, 0, 1);
                        break;
                }
                return true;
            }
            else
            {
                //MessageBox.Show($"文件还不存在，请安装主体文件", "文件不存在", //MessageBoxButton.OK, //MessageBoxImage.Warning, //MessageBoxResult.OK);
                return false;
            }
        }

        public async Task<int> Upload()
        {
            switch (CodeRoute.Substring(CodeRoute.LastIndexOf('.') + 1))
            {
                case "cpp":
                case "h":
                    Language = "cpp";
                    break;
                case "py":
                    Language = "python";
                    break;
                default:
                    return -8;
            }
            if (PlayerNum.Equals("nSelect"))
                return -9;
            return await web.UploadFiles(client, CodeRoute, Language, PlayerNum);
        }
        public bool WriteUsingOS()
        {
            string OS = "";
            switch (usingOS)
            {
                case UsingOS.Win:
                    OS = "win";
                    break;
                case UsingOS.Linux:
                    OS = "linux";
                    break;
                case UsingOS.OSX:
                    OS = "osx";
                    break;
            }
            return Web.WriteJson("OS", OS) == 0;
        }
        public UsingOS ReadUsingOS()
        {
            return Web.ReadJson("OS") switch
            {
                "linux" => UsingOS.Linux,
                "osx" => UsingOS.OSX,
                _ => UsingOS.Win,
            };
        }

        public void GetNewHash()
        {
            Cloud.Download(System.IO.Path.Combine(Data.FilePath, "hash.json"), "hash.json");
        }
        #endregion
    }


}
