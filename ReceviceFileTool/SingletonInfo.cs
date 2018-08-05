using System.Threading;
using System.Collections.Generic;
using System.Data;
using System.Collections.Concurrent;

namespace ReceviceFileTool
{
    public class SingletonInfo
    {
        private static SingletonInfo _singleton;
        public Dictionary<string, FileAll> FileDic;
      

        private SingletonInfo()                                                                 
        {
            FileDic = new Dictionary<string, FileAll>();
           
        }

   
        public static SingletonInfo GetInstance()
        {
            if (_singleton == null)
            {
                Interlocked.CompareExchange(ref _singleton, new SingletonInfo(), null);
            }
            return _singleton;
        }

   


    }
}