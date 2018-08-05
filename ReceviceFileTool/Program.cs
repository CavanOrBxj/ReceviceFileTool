using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace ReceviceFileTool
{
    class Program
    {

        public Thread dealThread;  //数据解析线程
        public NetServer netServer;
        ConcurrentQueue<byte[]> beforeAnalysisQueue;
        static object locker;
       static  string IP = "";
       static int port = 0;
       static log4net.ILog logInfo = log4net.LogManager.GetLogger("loginfo");
       private static IniFiles ini;
       static string SavePath = "";
       static string ftppath = "";
       static FTPHelper ftphelper;
        static void Main(string[] args)
        {

            string ftpserver;
            string ftpport;
            string ftpusername;
            string ftppwd;
           
            ini = new IniFiles(Path.Combine(Directory.GetCurrentDirectory(), "ReceiveTool.ini"));

            SavePath = ini.ReadValue("Local", "FileSavePath");
            if (!Directory.Exists(SavePath))//如果不存在就创建file文件夹
            {
                Directory.CreateDirectory(SavePath);

            }

            Program p = new Program();


            IP = args[0];
            port = Convert.ToInt32(args[1]);
            ftpserver = args[2];
            ftpport = args[3];
            ftpusername = args[4];
            ftppwd = args[5];
            ftppath = args[6];

            p.beforeAnalysisQueue = new ConcurrentQueue<byte[]>();
            p.Init();
            System.Timers.Timer aTimer = new System.Timers.Timer();
            aTimer.Elapsed += new System.Timers.ElapsedEventHandler(TimeEvent);
            aTimer.Interval = 2000;
            aTimer.Enabled = true;

            Console.WriteLine("文件接收工具启动！");
            logInfo.Info("文件接收工具启动");

            ftphelper = new FTPHelper(ftpserver, ftpusername, ftppwd);
            Console.ReadLine(); 
          
        }

        private void Init()
        {
            dealThread = new Thread(DealStatus);
            dealThread.Start();

            locker = new object();
            netServer = new NetServer((ushort)65533, (ushort)port, IP);
            netServer.UDPReceiveData += Server_UDPReceiveData;
            netServer.Start();
        }

        /// <summary>
        /// 数据解析
        /// </summary>
        public void DealStatus()
        {

            while (true)
            {
                try
                {
                    if (!beforeAnalysisQueue.IsEmpty)
                    {
                        byte[] data;
                        beforeAnalysisQueue.TryDequeue(out data); //拿出数据
                        string dadada = "";
                        for (int i = 0; i < data.Length; i++)
                        {
                            dadada += " " + data[i].ToString("X2");
                        }

                        if (data != null && data.Length > 0)
                        {
                            //解析数据
                            List<DataDetail> dataNew = HandlerQueue(data); //解析数据
                            if (dataNew != null)
                            {

                                string filename = dataNew[0].FileName;

                                string filepath = Directory.GetCurrentDirectory();
                                if (Directory.EnumerateFiles(filepath, filename + ".*", SearchOption.AllDirectories).Any())
                                {
                                    break;
                                }
                                if (SingletonInfo.GetInstance().FileDic.ContainsKey(filename))
                                {
                                    FileAll file = SingletonInfo.GetInstance().FileDic[filename];
                                    file.ReceiveTime = file.ReceiveTime.AddSeconds(3);
                                    foreach (DataDetail item in dataNew)
                                    {
                                        file.DataList.Add(item);
                                    }
                                }
                                else
                                {
                                    FileAll file = new FileAll();
                                    file.ReceiveTime = DateTime.Now.AddSeconds(3);

                                    file.DataList = new List<DataDetail>();
                                    foreach (DataDetail item in dataNew)
                                    {
                                        file.DataList.Add(item);
                                    }
                                    SingletonInfo.GetInstance().FileDic.Add(filename, file);
                                }
                            }
                        }
                    }
                    else
                    {
                    }

                    Thread.Sleep(200);
                }
                catch (Exception ex)
                {
                    continue;
                }
            }
        }

        /// <summary>
        /// 将数据加入缓存
        /// </summary>
        public void Enqueue(byte[] data)
        {
            if (data != null && data.Length > 0)
            {
                logInfo.Info("收到数据文件");
                beforeAnalysisQueue.Enqueue(data);
            }
        }


        /// <summary>
        /// 解析终端设备工作状态
        /// </summary>
        /// <param name="data"></param>
        /// <returns>设备状态</returns>
        private List<DataDetail> HandlerQueue(byte[] datare)
        {
            byte[] data = datare;
            if (data.Length < 13) return null;

            List<DataDetail> DDlist = new List<DataDetail>();
            try
            {
                string pp = "";
                for (int i = 0; i < data.Length; i++)
                {
                    pp += " " + data[i].ToString("X2");
                }


                string pp1 = "";
                for (int i = 0; i < data.Length; i++)
                {
                    pp1 += data[i].ToString("X2");
                }



                var msgType = Convert.ToChar(data[0]);
                if (msgType == '&')
                {
                    //帧头占12字节
                }
                else if (msgType == '%')
                {
                    List<byte[]> bodyList = GetBodtList(data);


                    foreach (byte[] singledataBody in bodyList)
                    {
                        //帧头占18字节
                        byte[] dataBody = singledataBody;


                        //判断CRC是否对应
                        var array1 = CalmCRC.GetCRC16(dataBody.Take(dataBody.Length - 2).ToArray(), true);
                        var array2 = dataBody.Skip(dataBody.Length - 2).ToArray();
                        if (EqualsArray(array1, array2))
                        {
                            string allengthstr = Convert.ToString((int)dataBody[2], 16).PadLeft(2, '0') + Convert.ToString((int)dataBody[1], 16).PadLeft(2, '0');
                            int AlldataLength = Convert.ToInt32(allengthstr, 16);
                            int l = Convert.ToInt32(Convert.ToString((int)dataBody[5], 16).PadLeft(2, '0'), 16);//第一个数据段数据部分的长度

                            List<byte> Section1List = new List<byte>();
                            for (int i = 0; i < l; i++)
                            {
                                Section1List.Add(dataBody[6 + i]);
                            }

                            DataDetail data1 = Deal(Section1List);
                            DDlist.Add(data1);

                            while (AlldataLength > l + 3)
                            {
                                int SectionNdataLength = Convert.ToInt32(Convert.ToString((int)dataBody[l + 6 + 2], 16).PadLeft(2, '0'), 16);
                                List<byte> SectionList = new List<byte>();
                                for (int i = 0; i < SectionNdataLength; i++)
                                {
                                    SectionList.Add(dataBody[l + 6 + 3 + i]);
                                }

                                DataDetail datatmp = Deal(SectionList);
                                DDlist.Add(datatmp);
                                l = l + 3 + Convert.ToInt32(Convert.ToString((int)dataBody[l + 6 + 2], 16).PadLeft(2, '0'), 16);
                            }
                        }
                    }

                }
                return DDlist;
            }
            catch (Exception ex)
            {
                return null;
            }
        }



        private List<byte[]> GetBodtList(byte[] data)
        {
            List<byte[]> bodyList = new List<byte[]>();
            if (data.Length == 0) return null;


            int l = data.Length;

            while (l > 0)
            {
                byte[] nimei = data.Skip(data.Length - l).ToArray();
                byte[] datatmp = nimei.Skip(18).ToArray();
                if (datatmp.Length > 9)//一个完整帧长度
                {
                    int singledataLength = Convert.ToInt32(datatmp[2].ToString("x2") + datatmp[1].ToString("x2"), 16);
                    List<byte> singledata = new List<byte>();

                    for (int i = 0; i < singledataLength + 5; i++)
                    {
                        singledata.Add(datatmp[i]);
                    }

                    bodyList.Add(singledata.ToArray());
                    l = l - singledata.Count - 18;
                }
            }
            return bodyList;

        }


        public DataDetail Deal(List<byte> dtlist)
        {
            byte[] bdata = dtlist.ToArray();
            DataDetail datadetail = new DataDetail();
            datadetail.PhysicalAddressLength = Convert.ToInt32(Convert.ToString((int)bdata[0], 16).PadLeft(2, '0'), 16);


            List<byte> PhysicalAddressList = new List<byte>();
            for (int i = 0; i < datadetail.PhysicalAddressLength; i++)//物理码地址 5
            {
                PhysicalAddressList.Add(bdata[1 + i]);
            }
            byte[] PhysicalAddressArray = PhysicalAddressList.ToArray();
            string phyaddr = "";
            for (int i = 0; i < PhysicalAddressArray.Length; i++)
            {
                phyaddr += PhysicalAddressArray[i].ToString("x2");
            }
            datadetail.PhysicalAddress = phyaddr;



            List<byte> FileNameList = new List<byte>();
            for (int i = 0; i < 33; i++)
            {
                FileNameList.Add(bdata[1 + datadetail.PhysicalAddressLength + i]);
            }
            byte[] FileNameArray = FileNameList.ToArray();
            datadetail.FileName = System.Text.Encoding.ASCII.GetString(FileNameArray);



            List<byte> PackNumList = new List<byte>();
            for (int i = 0; i < 4; i++)
            {
                PackNumList.Add(bdata[33 + 1 + datadetail.PhysicalAddressLength + i]);
            }
            PackNumList.Reverse();
            byte[] PackNumArray = PackNumList.ToArray();
            datadetail.PackageNum = ToHexString(PackNumArray);


            datadetail.DataLength = Convert.ToInt32(bdata[1 + datadetail.PhysicalAddressLength + 33 + 5].ToString("x2") + bdata[1 + datadetail.PhysicalAddressLength + 33 + 4].ToString("x2"), 16);

            List<byte> AudioDataList = new List<byte>();
            for (int i = 0; i < datadetail.DataLength; i++)
            {
                AudioDataList.Add(bdata[33 + 1 + datadetail.PhysicalAddressLength + 5 + 1 + i]);
            }
            byte[] AudioDataArray = AudioDataList.ToArray();
            datadetail.AudioData = AudioDataArray;
            return datadetail;

        }


        public string ToHexString(byte[] bytes) // 0xae00cf => "AE00CF "
        {
            string hexString = string.Empty;

            if (bytes != null)
            {

                StringBuilder strB = new StringBuilder();

                for (int i = 0; i < bytes.Length; i++)
                {

                    strB.Append(bytes[i].ToString("X2"));

                }

                hexString = strB.ToString();

            } return hexString;

        }


        /// <summary>
        /// 获取帧体
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] GetFrameBody(byte[] data)
        {
            if (data.Length == 0) return null;
            var msgType = Convert.ToChar(data[0]);
            if (msgType == '&')
            {
                //帧头占12字节
                return data.Skip(12).ToArray();
            }
            else if (msgType == '%')
            {
                //帧头占18字节
                return data.Skip(18).ToArray();
            }
            return null;
        }


        public static bool EqualsArray(Array a, Array b)
        {
            if (a == null && b == null)
            {
                return true;
            }
            else if (a == null)
            {
                return false;
            }
            else if (b == null)
            {
                return false;
            }
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (!a.GetValue(i).Equals(b.GetValue(i)))
                {
                    return false;
                }
            }
            return true;
        }



        private void Server_UDPReceiveData(object sender, SocketDataEventArgs e)
        {
            if (e.Data != null && e.Data.Length > 0)
            {
              //  logInfo.Info("文件接收工具启动");
                Enqueue(e.Data);
            }
        }

        private static void TimeEvent(object source, ElapsedEventArgs e)
        {
            if (SingletonInfo.GetInstance().FileDic.Count > 0)
            {
                lock (locker)
                {
                    foreach (var item in SingletonInfo.GetInstance().FileDic)
                    {
                        if (item.Value.DataList.Count > 0)
                        {
                            if (DateTime.Compare(DateTime.Now, item.Value.ReceiveTime) > 0)//接收超时了 则立即组装成文件
                            {
                                List<DataDetail> CompleteList = SortList(item.Value.DataList);
                                Thread SaveFile = new Thread(new ParameterizedThreadStart(SaveFiletoMP3));
                                SaveFile.Start((object)CompleteList);
                            }
                            else
                            {
                                bool flag = false;
                             
                                //没有超时 只需考虑接收完成的情况
                                foreach (DataDetail dd in item.Value.DataList)
                                {
                                    if (dd.PackageNum == "FFFFFFFF")
                                    {
                                        flag = true;
                                        break;
                                    }
                                }

                                if (flag)
                                {
                                    List<DataDetail> CompleteList = SortList(item.Value.DataList);
                                    Thread SaveFile = new Thread(new ParameterizedThreadStart(SaveFiletoMP3));
                                    SaveFile.Start((object)CompleteList);

                                }
                            }
                        }
                    }
                }
            }
        }

        private static List<DataDetail> SortList(List<DataDetail> OldList)
        {
            List<DataDetail> NewList = new List<DataDetail>();
            if (OldList.Count > 0)
            {
                bool flag = false;//判断有无结尾帧
                int local = 0;

                for (int i = 0; i < OldList.Count; i++)
                {
                    if (OldList[i].PackageNum == "FFFFFFFF")
                    {

                        flag = true;
                        local = i;
                    }
                }

                if (flag)
                {
                    //有结尾帧
                    DataDetail LastOne = OldList[local];

                    OldList.Remove(LastOne);

                    int gap = OldList.Count / 2; //取长度的一半
                    bool HasChange = true;
                    while (gap > 1 || HasChange)
                    {
                        HasChange = false;
                        for (int i = 0; i + gap < OldList.Count; i++)
                        {
                            if (Convert.ToInt64(OldList[i].PackageNum, 16) > Convert.ToInt64(OldList[i + gap].PackageNum, 16))
                            {
                                DataDetail temp = OldList[i];
                                OldList[i] = OldList[i + gap];
                                OldList[i + gap] = temp;//交换并设置下一轮循环
                                HasChange = true;
                            }//当条件不满足的时候证明该间距内没有变化（有序）了
                            if (gap > 1)
                            {
                                gap /= 2;
                            }
                        }
                    }
                    OldList.Add(LastOne);
                }
                else
                {
                    //无结尾帧
                    int gap = OldList.Count / 2; //取长度的一半
                    bool HasChange = true;
                    while (gap > 1 || HasChange)
                    {
                        HasChange = false;
                        for (int i = 0; i + gap < OldList.Count; i++)
                        {
                            if (Convert.ToInt64(OldList[i].PackageNum, 16) > Convert.ToInt64(OldList[i + gap].PackageNum, 16))
                            {
                                DataDetail temp = OldList[i];
                                OldList[i] = OldList[i + gap];
                                OldList[i + gap] = temp;//交换并设置下一轮循环
                                HasChange = true;
                            }//当条件不满足的时候证明该间距内没有变化（有序）了
                            if (gap > 1)
                            {
                                gap /= 2;
                            }
                        }
                    }
                }
            }

            NewList = OldList;
            return NewList;
        }

        private static void SaveFiletoMP3(object ob)
        {
            try
            {
                List<DataDetail> CompleteList = (List<DataDetail>)ob;
                List<byte> DataList = new List<byte>();
                if (CompleteList.Count > 0)
                {
                    foreach (DataDetail item in CompleteList)
                    {
                        foreach (byte bt in item.AudioData)
                        {
                            DataList.Add(bt);
                        }
                    }
                    string filename = CompleteList[0].FileName + ".mp3";
                    byte[] buffer = DataList.ToArray();
                    string path = SavePath + "\\" + filename;
                    FileStream fs = new FileStream(path, FileMode.Create);//新建文件
                    fs.Write(buffer, 0, buffer.Length);
                    fs.Flush();
                    fs.Close();
                    SingletonInfo.GetInstance().FileDic.Remove(CompleteList[0].FileName);

                    Thread.Sleep(500);
                    #region   ftp上传
                    ftphelper.UploadFile(path, ftppath);
                    #endregion

                }
            }
            catch (Exception ex)
            {

                throw;
            }
        }



    }
}
