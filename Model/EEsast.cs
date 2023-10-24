using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace installer.Model
{
    [Serializable]
    record LoginResponse
    {
        // Map `Token` to `token` when serializing

        public string Token { get; set; } = "";
    }

    class EEsast
    {
        public enum language { cpp, py };
        public static string logintoken = "";
        async public Task<int> LoginToEEsast(HttpClient client, string useremail, string password)
        {
            string token = "";
            try
            {
                using (var response = await client.PostAsync("https://api.eesast.com/users/login", JsonContent.Create(new
                {
                    email = useremail,
                    password = password,
                })))
                {
                    switch (response.StatusCode)
                    {
                        case System.Net.HttpStatusCode.OK:
                            //Console.WriteLine("Success login");
                            token = (System.Text.Json.JsonSerializer.Deserialize(await response.Content.ReadAsStreamAsync(), typeof(LoginResponse), new JsonSerializerOptions()
                            {
                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            }) as LoginResponse)
                                        ?.Token ??
                                    throw new Exception("no token!");
                            logintoken = token;
                            SaveToken();
                            var info = Helper.DeserializeJson1<Dictionary<string, string>>(await response.Content.ReadAsStringAsync());
                            Downloader.UserInfo._id = info["_id"];
                            Downloader.UserInfo.email = info["email"];
                            break;

                        default:
                            int code = ((int)response.StatusCode);
                            //Console.WriteLine(code);
                            if (code == 401)
                            {
                                //Console.WriteLine("邮箱或密码错误！");
                                return -1;
                            }
                            break;
                    }
                    return 0;
                }
            }
            catch
            {
                return -2;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="client">http client</param>
        /// <param name="tarfile">代码源位置</param>
        /// <param name="type">编程语言，格式为"cpp"或"python"</param>
        /// <param name="plr">第x位玩家，格式为"player_x"</param>
        /// <returns>-1:tokenFail;-2:FileNotExist;-3:CosFail;-4:loginTimeout;-5:Fail;-6:ReadFileFail;-7:networkError</returns>
        async public Task<int> UploadFiles(HttpClient client, string tarfile, string type, string plr)    //用来上传文件
        {
            if (!ReadToken())   //读取token失败
            {
                return -1;
            }
            try
            {
                string content;
                client.DefaultRequestHeaders.Authorization = new("Bearer", logintoken);
                if (!File.Exists(tarfile))
                {
                    //Console.WriteLine("文件不存在！");
                    return -2;
                }
                using FileStream fs = new FileStream(tarfile, FileMode.Open, FileAccess.Read);
                using StreamReader sr = new StreamReader(fs);
                content = sr.ReadToEnd();
                string targetUrl = $"https://api.eesast.com/static/player?team_id={await GetTeamId()}";
                using (var response = await client.GetAsync(targetUrl))
                {
                    switch (response.StatusCode)
                    {
                        case System.Net.HttpStatusCode.OK:

                            var res = Helper.DeserializeJson1<Dictionary<string, string>>(await response.Content.ReadAsStringAsync());
                            string appid = "1255334966";                    // 设置腾讯云账户的账户标识（APPID）
                            string region = "ap-beijing";                   // 设置一个默认的存储桶地域
                            CosXmlConfig config = new CosXmlConfig.Builder()
                                                      .IsHttps(true)        // 设置默认 HTTPS 请求
                                                      .SetAppid(appid)      // 设置腾讯云账户的账户标识 APPID
                                                      .SetRegion(region)    // 设置一个默认的存储桶地域
                                                      .SetDebugLog(true)    // 显示日志
                                                      .Build();             // 创建 CosXmlConfig 对象
                            string tmpSecretId = res["TmpSecretId"];        //"临时密钥 SecretId";
                            string tmpSecretKey = res["TmpSecretKey"];      //"临时密钥 SecretKey";
                            string tmpToken = res["SecurityToken"];         //"临时密钥 token";
                            long tmpExpiredTime = Convert.ToInt64(res["ExpiredTime"]);  //临时密钥有效截止时间，精确到秒
                            QCloudCredentialProvider cosCredentialProvider = new DefaultSessionQCloudCredentialProvider(
                                tmpSecretId, tmpSecretKey, tmpExpiredTime, tmpToken
                            );
                            // 初始化 CosXmlServer
                            CosXmlServer cosXml = new CosXmlServer(config, cosCredentialProvider);

                            // 初始化 TransferConfig
                            TransferConfig transferConfig = new TransferConfig();

                            // 初始化 TransferManager
                            TransferManager transferManager = new TransferManager(cosXml, transferConfig);

                            string bucket = "eesast-1255334966"; //存储桶，格式：BucketName-APPID
                            string cosPath = $"/THUAI6/{GetTeamId()}/{type}/{plr}"; //对象在存储桶中的位置标识符，即称对象键
                            string srcPath = tarfile;//本地文件绝对路径

                            // 上传对象
                            COSXMLUploadTask uploadTask = new COSXMLUploadTask(bucket, cosPath);
                            uploadTask.SetSrcPath(srcPath);

                            uploadTask.progressCallback = delegate (long completed, long total)
                            {
                                //Console.WriteLine(string.Format("progress = {0:##.##}%", completed * 100.0 / total));
                            };

                            try
                            {
                                COSXMLUploadTask.UploadTaskResult result = await transferManager.UploadAsync(uploadTask);
                                //Console.WriteLine(result.GetResultInfo());
                                string eTag = result.eTag;
                                //到这里应该是成功了，但是因为我没有试过，也不知道具体情况，可能还要根据result的内容判断
                            }
                            catch (Exception)
                            {
                                return -3;
                            }

                            break;
                        case System.Net.HttpStatusCode.Unauthorized:
                            //Console.WriteLine("您未登录或登录过期，请先登录");
                            return -4;
                        default:
                            //Console.WriteLine("上传失败！");
                            return -5;
                    }
                }
            }
            catch (IOException)
            {
                //Console.WriteLine("文件读取错误！请检查文件是否被其它应用占用！");
                return -6;
            }
            catch
            {
                //Console.WriteLine("请求错误！请检查网络连接！");
                return -7;
            }
            return 0;
        }

        async public Task UserDetails(HttpClient client)  // 用来测试访问网站
        {
            if (!ReadToken())  // 读取token失败
            {
                return;
            }
            try
            {
                client.DefaultRequestHeaders.Authorization = new("Bearer", logintoken);
                Console.WriteLine(logintoken);
                using (var response = await client.GetAsync("https://api.eesast.com/application/info"))  // JsonContent.Create(new
                                                                                                         //{

                //})))
                {
                    switch (response.StatusCode)
                    {
                        case System.Net.HttpStatusCode.OK:
                            Console.WriteLine("Require OK");
                            Console.WriteLine(await response.Content.ReadAsStringAsync());
                            break;
                        default:
                            int code = ((int)response.StatusCode);
                            if (code == 401)
                            {
                                Console.WriteLine("您未登录或登录过期，请先登录");
                            }
                            return;
                    }
                }
            }
            catch
            {
                Console.WriteLine("请求错误！请检查网络连接！");
            }
        }

        public void SaveToken()  // 保存token
        {
            string savepath = System.IO.Path.Combine(Data.dataPath, "THUAI6.json");
            try
            {
                string json;
                Dictionary<string, string> dict = new Dictionary<string, string>();
                using FileStream fs = new FileStream(savepath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                using (StreamReader r = new StreamReader(fs))
                {
                    json = r.ReadToEnd();
                    if (json == null || json == "")
                    {
                        json += @"{""THUAI6""" + ":" + @"""2023""}";
                    }
                    dict = Utils.DeserializeJson1<Dictionary<string, string>>(json);
                    if (dict.ContainsKey("token"))
                    {
                        dict.Remove("token");
                    }
                    dict.Add("token", logintoken);
                }
                using FileStream fs2 = new FileStream(savepath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                using StreamWriter sw = new StreamWriter(fs2);
                fs2.SetLength(0);
                sw.Write(JsonConvert.SerializeObject(dict));   //将token写入文件
            }
            catch (DirectoryNotFoundException)
            {
                Console.WriteLine("保存token时未找到下载器地址！请检查下载器是否被移动！");
            }
            catch (PathTooLongException)
            {
                Console.WriteLine("下载器的路径名太长！请尝试移动下载器！");
            }
            catch (ArgumentNullException)
            {
                Console.WriteLine("下载器路径初始化失败！");
            }
            catch (IOException)
            {
                Console.WriteLine("写入token.dat发生冲突！请检查token.dat是否被其它程序占用！");
            }
        }

        public static int WriteJson(string key, string data)
        {
            try
            {
                string savepath = System.IO.Path.Combine(Data.dataPath, "THUAI6.json");
                FileStream fs = new FileStream(savepath, FileMode.Open, FileAccess.ReadWrite);
                StreamReader sr = new StreamReader(fs);
                string json = sr.ReadToEnd();
                if (json == null || json == "")
                {
                    json += @"{""THUAI6""" + ":" + @"""2023""}";
                }
                Dictionary<string, string> dict = new Dictionary<string, string>();
                dict = Utils.DeserializeJson1<Dictionary<string, string>>(json);
                if (!dict.ContainsKey(key))
                {
                    dict.Add(key, data);
                }
                else
                {
                    dict[key] = data;
                }
                sr.Close();
                fs.Close();
                FileStream fs2 = new FileStream(savepath, FileMode.Open, FileAccess.ReadWrite);
                StreamWriter sw = new StreamWriter(fs2);
                sw.WriteLine(JsonConvert.SerializeObject(dict));
                sw.Close();
                fs2.Close();
                return 0;//成功
            }
            catch
            {
                return -1;//失败
            }
        }

        public static string? ReadJson(string key)
        {
            try
            {
                string savepath = System.IO.Path.Combine(Data.dataPath, "THUAI6.json");
                FileStream fs = new FileStream(savepath, FileMode.Open, FileAccess.Read);
                StreamReader sr = new StreamReader(fs);
                string json = sr.ReadToEnd();
                Dictionary<string, string> dict = new Dictionary<string, string>();
                if (json == null || json == "")
                {
                    json += @"{""THUAI6""" + ":" + @"""2023""}";
                }
                dict = Utils.DeserializeJson1<Dictionary<string, string>>(json);
                fs.Close();
                sr.Close();
                return dict[key];

            }
            catch
            {
                return null;  //文件不存在或者已被占用
            }
        }

        public bool ReadToken()  // 读取token
        {
            try
            {
                string json;
                Dictionary<string, string> dict = new Dictionary<string, string>();
                string savepath = System.IO.Path.Combine(Data.dataPath, "THUAI6.json");
                using FileStream fs = new FileStream(savepath, FileMode.Open, FileAccess.Read);
                using StreamReader sr = new StreamReader(fs);

                json = sr.ReadToEnd();
                if (json == null || json == "")
                {
                    json += @"{""THUAI6""" + ":" + @"""2023""}";
                }
                dict = Utils.DeserializeJson1<Dictionary<string, string>>(json);
                if (!dict.ContainsKey("token"))
                {
                    return false;
                }
                else
                {
                    logintoken = dict["token"];
                    return true;
                }
            }
            catch (DirectoryNotFoundException)
            {
                Console.WriteLine("读取token时未找到下载器地址！请检查下载器是否被移动！");
                return false;
            }
            catch (FileNotFoundException)
            {
                //没有登陆
                Console.WriteLine("请先登录！");
                return false;
            }
            catch (PathTooLongException)
            {
                Console.WriteLine("下载器的路径名太长！请尝试移动下载器！");
                return false;
            }
            catch (ArgumentNullException)
            {
                Console.WriteLine("下载器路径初始化失败！");
                return false;
            }
            catch (IOException)
            {
                Console.WriteLine("写入token.dat发生冲突！请检查token.dat是否被其它程序占用！");
                return false;
            }
        }

        async public Task<string> GetTeamId()
        {
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.eesast.com/dev/v1/graphql");
            request.Headers.Add("x-hasura-admin-secret", "hasuraDevAdminSecret");
            //var content = new StringContent($@"
            //    {{
            //        ""query"": ""query MyQuery {{contest_team_member(where: {{user_id: {{_eq: \""{Downloader.UserInfo._id}\""}}}}) {{ team_id  }}}}"",
            //        ""variables"": {{}},
            //    }}", null, "application/json");
            var content = new StringContent("{\"query\":\"query MyQuery {\\r\\n  contest_team_member(where: {user_id: {_eq: \\\"" + Downloader.UserInfo._id + "\\\"}}) {\\r\\n    team_id\\r\\n  }\\r\\n}\",\"variables\":{}}", null, "application/json");
            request.Content = content;
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var info = await response.Content.ReadAsStringAsync();
            var s1 = Utils.DeserializeJson1<Dictionary<string, object>>(info)["data"];
            var s2 = Utils.DeserializeJson1<Dictionary<string, List<object>>>(s1.ToString() ?? "")["contest_team_member"];
            var sres = Utils.DeserializeJson1<Dictionary<string, string>>(s2[0].ToString() ?? "")["team_id"];
            return sres;
        }
        async public Task<string> GetUserId(string learnNumber)
        {
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.eesast.com/dev/v1/graphql");
            request.Headers.Add("x-hasura-admin-secret", "hasuraDevAdminSecret");
            var content = new StringContent("{\"query\":\"query MyQuery {\r\n  user(where: {id: {_eq: \""
                + learnNumber + "\"}}) {\r\n    _id\r\n  }\r\n}\r\n\",\"variables\":{}}", null, "application/json");
            request.Content = content;
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }


    }
}
