using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//NuGet\Install-Package System.Drawing.Common -Version 7.0.0 

namespace _18203Proj2
{
    public class LocalCache //with LRU
    {
        private Dictionary<string, Bitmap> cache;
        private int max;
        private LinkedList<string> requests; //poslednji element je kandidat za izbacivanje
        private LinkedList<string> current; //resurs koji se vec trazi sada

        public LocalCache()
        {
            cache = new Dictionary<string, Bitmap>();
            this.max = 3; //favicon.ico uvek prvi
            this.requests = new LinkedList<string>();
            this.current = new LinkedList<string>();
        }

        public bool ContainReq(string request)
        {
            if (this.cache.ContainsKey(request))
                return true;
            return false;
        }

        public void AddReq(string request, Bitmap bmp)
        {
            if (this.cache.ContainsKey(request))
            {
                this.requests.Remove(request);
            }
            else
            {
                if (this.cache.Count >= max)
                {
                    string last = this.requests.Last();
                    this.requests.Remove(last);
                    this.cache.Remove(last);
                    Console.WriteLine($"Maximum cache size succeeded, request {last} removed.");

                }
                this.cache.TryAdd(request, bmp);
            }
            this.requests.AddFirst(request);
        }

        public bool TryGetValue(string request, out Bitmap value)
        {
            bool status = this.cache.TryGetValue(request, out value);
            
            if (status && (requests != null))
            {
                bool x = this.requests.Remove(request);
                this.requests.AddFirst(request);
            }

            return status;
        }

        public void AddCurrent(string request)
        {
            this.current.AddLast(request);
        }

        public bool HasCurrent(string request)
        {
            return this.current.Contains(request) ? true : false;
        }

        public void RemoveCurrent(string request)
        {
            this.current.Remove(request);
        }
    }
}
