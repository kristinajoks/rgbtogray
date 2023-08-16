using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace _18203Proj1
{
    public class LocalCache //with LRU
    {
        private Dictionary<string, Bitmap> cache;
        private int max;
        private LinkedList<string> requests; //poslednji element je kandidat za izbacivanje

        public LocalCache ()
        {
            this.cache = new Dictionary<string, Bitmap> ();
            this.max = 3; //favicon.ico uvek prvi
            this.requests = new LinkedList<string> (); 
        }

        public bool containReq(string request)
        {
            if (this.cache.ContainsKey(request))
                return true;            
            return false;
        }

        public void addReq(string request, Bitmap bmp) {
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

        public bool tryGetValue(string request, out Bitmap value)
        {
            bool status = this.cache.TryGetValue(request, out value);

            if (status)
            {
                bool x =this.requests.Remove(request);
                this.requests.AddFirst(request);
            }

            return status;
        }

    }
}
