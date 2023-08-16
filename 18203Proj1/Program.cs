using System.Net;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace _18203Proj1
{
    class Program
    {
        static HttpListener listener = new HttpListener();
        static LocalCache cache = new LocalCache();
        static Stopwatch stopwatch = new Stopwatch();

        //singlethreaded server + multithreaded picture processing
        public static void ServerDivided()
        {
            listener.Prefixes.Add("http://localhost:5050/");
            listener.Start();

            Console.WriteLine("Listening on port 5050...\n");

            string localPath = "C:\\Users\\krist\\sysprog\\18203Proj1\\photos\\";
            cache.addReq("http://localhost:5050/favicon.ico", new Bitmap($"{localPath}favicon.ico"));

            while (true)
            {
                try
                {
                    HttpListenerContext context = listener.GetContextAsync().Result;
                    HttpListenerRequest request = context.Request;

                    LogRequest(request);

                    stopwatch.Restart();

                    if (request.Url == null)
                    {
                        throw new HttpRequestException();
                    }

                    HttpListenerResponse response = context.Response;
                    response.Headers.Set("Content-type", "image/jpg");

                    Stream stream = response.OutputStream;
                    Bitmap final;

                    if (cache.containReq(request.Url.ToString()))
                    {
                        Console.WriteLine($"Resource {request.RawUrl} found in cache\n");
                        cache.tryGetValue(request.Url.ToString(), out final);
                    }
                    else
                    {
                        Console.WriteLine($"Resource {request.RawUrl} not found in cache\n");
                        string path = localPath + request.Url.LocalPath;

                        byte[] buffer = File.ReadAllBytes(path);

                        //if (request.Url.AbsolutePath == "/favicon.ico") 
                        //{
                        //    stream.Write(buffer, 0, buffer.Length);
                        //    return;
                        //}

                        Bitmap imgFile = new Bitmap(path);
                        Bitmap[,] tiles = MakeTiles((object)imgFile);
                        Bitmap[,] res = ParallelImageProcess(tiles);
                        final = JoinTiles(res, imgFile);

                        cache.addReq(request.Url.ToString(), final);
                    }

                    byte[] resArray = ToByteArr(final, System.Drawing.Imaging.ImageFormat.Bmp);
                    response.ContentLength64 = resArray.Length;
                    stream.Write(resArray, 0, resArray.Length);

                    stopwatch.Stop();

                    LogResponse(response, request.RawUrl);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        public static object cacheLocker = new object();

        //multithreaded server + 2 way picture processing
        public static void ServerPool()
        {
            listener.Prefixes.Add("http://localhost:5050/");
            listener.Start();

            Console.WriteLine("Listening on port 5050...\n");

            string localPath = "C:\\Users\\krist\\sysprog\\18203Proj1\\photos\\";
            cache.addReq("http://localhost:5050/favicon.ico", new Bitmap($"{localPath}favicon.ico"));

            while (true)
            {
                ThreadPool.QueueUserWorkItem((state) =>
                {
                    try
                    {
                        HttpListenerContext context = listener.GetContextAsync().Result;
                        HttpListenerRequest request = context.Request;

                        LogRequest(request);

                        stopwatch.Restart();

                        if (request.Url == null)
                        {
                            throw new HttpRequestException();
                        }

                        HttpListenerResponse response = context.Response;
                        response.Headers.Set("Content-type", "image/jpg");
                        Stream stream = response.OutputStream;
                        Bitmap final;

                        bool found = false;
                        lock (cacheLocker)
                        {
                            found = cache.containReq(request.Url.ToString());
                        }

                        if (found)
                        {
                            Console.WriteLine($"Resource {request.RawUrl} found in cache\n");
                            
                            lock (cacheLocker)
                            {
                                cache.tryGetValue(request.Url.ToString(), out final);
                            }
                        }
                        else
                        {                            
                            Console.WriteLine($"Resource {request.RawUrl} not found in cache\n");
                            
                            string localPath = "C:\\Users\\krist\\sysprog\\18203Proj1\\photos\\";
                            string path = localPath + request.Url.LocalPath;
                            byte[] buffer = File.ReadAllBytes(path);

                            //if (request.Url.AbsolutePath == "/favicon.ico")
                            //{
                            //    stream.Write(buffer, 0, buffer.Length);
                            //    return;
                            //}

                            Bitmap imgFile = new Bitmap(path);
                            //final = SingleThreadedImageProcess(imgFile);

                            Bitmap[,] tiles = MakeTiles((object)imgFile);
                            Bitmap[,] res = MultithreadedImageProcess(tiles);
                            final = JoinTiles(res, imgFile);

                            lock (cacheLocker)
                            {
                                cache.addReq(request.Url.ToString(), final);
                            }
                        }

                        byte[] resArray = ToByteArr(final, System.Drawing.Imaging.ImageFormat.Bmp);
                        response.ContentLength64 = resArray.Length;
                        stream.Write(resArray, 0, resArray.Length);

                        stopwatch.Stop();
                                                
                        LogResponse(response, request.RawUrl);                        
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                });
            }
        }

        #region logging
        public static void LogRequest(HttpListenerRequest request)
        {
            Console.WriteLine("--------------------------------------------------------------");
            Console.WriteLine($"Recieved request for: {request.RawUrl} by: {request.UserHostName}");
            Console.WriteLine($"Http method: {request.HttpMethod}");
            Console.WriteLine($"Protocol version: {request.ProtocolVersion}\n");
            Console.WriteLine($"Headers: {request.Headers}");
            Console.WriteLine("--------------------------------------------------------------");
        }

        public static void LogResponse(HttpListenerResponse response, string resource)
        {
            Console.WriteLine("--------------------------------------------------------------");
            Console.WriteLine($"{resource} retrieved");
            Console.WriteLine($"Response status: {response.StatusCode} {response.StatusDescription}");
            Console.WriteLine($"Response time: {stopwatch.Elapsed}");
            Console.WriteLine($"Protocol version: {response.ProtocolVersion}");
            Console.WriteLine($"Content type: {response.ContentType}");
            Console.WriteLine("--------------------------------------------------------------");
        }
        #endregion

        public static byte[] ToByteArr(Bitmap bitmap, System.Drawing.Imaging.ImageFormat format)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                return ms.ToArray();
            }
        }

        #region poolImageManipulation
        public static Bitmap[,] MakeTiles(object bmap)
        {
            Bitmap bmp = (Bitmap)bmap;
            Size tilesize = new Size(bmp.Width / 4, bmp.Height / 3);
            Bitmap[,] bmparray = new Bitmap[4, 3];

            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    //srcTile
                    Rectangle movingTile = new Rectangle(i * tilesize.Width, j * tilesize.Height, tilesize.Width, tilesize.Height);

                    bmparray[i, j] = new Bitmap(tilesize.Width, tilesize.Height);

                    //ubacivanje slika u bmparray
                    using (Graphics canvas = Graphics.FromImage(bmparray[i, j]))
                    {
                        canvas.DrawImage(bmp, new Rectangle(0, 0, tilesize.Width, tilesize.Height), movingTile, GraphicsUnit.Pixel);
                    }
                }
            }

            return bmparray;
        }
        
        public static Bitmap[,] ParallelImageProcess(Bitmap[,] bmp)
        {
            int thNum = Environment.ProcessorCount;
            Console.WriteLine($"Number of threads processing image: {thNum}\n");

            foreach (Bitmap bitmap in bmp)
            {
                ThreadPool.QueueUserWorkItem((state) =>
                {
                    try
                    {
                        lock (bitmap)
                        {
                            int width = bitmap.Width;
                            int height = bitmap.Height;

                            for (int i = 0; i < width; i++)
                            {
                                for (int j = 0; j < height; j++)
                                {
                                    Color oldPixel = bitmap.GetPixel(i, j);

                                    int grayScale = (int)((oldPixel.R * 0.229) + (oldPixel.G * 0.587) + (oldPixel.B * 0.114));
                                    Color newPixel = Color.FromArgb(grayScale, grayScale, grayScale);

                                    bitmap.SetPixel(i, j, newPixel);
                                }
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                });
            }

            bool done = false; 
            while (!done)
            {
                Thread.Sleep(1000); 
                done = ThreadPool.PendingWorkItemCount == 0;
            }

            return bmp;
        }

        public static Bitmap JoinTiles(Bitmap[,] bitmaps, Bitmap original)
        {
            Size tilesize = new Size(original.Width / 4, original.Height / 3);
            Bitmap res = new Bitmap(original.Width, original.Height);

            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    //srcTile
                    Rectangle movingTile = new Rectangle(0, 0, tilesize.Width, tilesize.Height);


                    //ubacivanje slika u rezultujucu mapu
                    using (Graphics canvas = Graphics.FromImage(res))
                    {
                        canvas.DrawImage(bitmaps[i, j], new Rectangle(i * tilesize.Width, j * tilesize.Height, tilesize.Width, tilesize.Height), movingTile, GraphicsUnit.Pixel);
                    }
                }
            }

            return res;
        }
        #endregion

        public static Bitmap SingleThreadedImageProcess(Bitmap bmp)
        {
                int width = bmp.Width;
                int height = bmp.Height;

                for (int i = 0; i < width; i++)
                {
                    for (int j = 0; j < height; j++)
                    {
                        Color oldPixel = bmp.GetPixel(i, j);

                        int grayScale = (int)((oldPixel.R * 0.229) + (oldPixel.G * 0.587) + (oldPixel.B * 0.114));
                        Color newPixel = Color.FromArgb(grayScale, grayScale, grayScale);

                        bmp.SetPixel(i, j, newPixel);
                    }
                }                        
            
            return bmp;
        }

        public static Bitmap[,] MultithreadedImageProcess(Bitmap[,] bmp)
        {
            int thNum = Environment.ProcessorCount;
            Console.WriteLine($"Number of threads processing image: {thNum}\n");

            List<Thread> threads = new List<Thread>();

            foreach (Bitmap bitmap in bmp)
            {
                Thread worker = new Thread((state) =>
                {
                    try
                    {
                        lock (bitmap)
                        {
                            int width = bitmap.Width;
                            int height = bitmap.Height;

                            for (int i = 0; i < width; i++)
                            {
                                for (int j = 0; j < height; j++)
                                {
                                    Color oldPixel = bitmap.GetPixel(i, j);

                                    int grayScale = (int)((oldPixel.R * 0.229) + (oldPixel.G * 0.587) + (oldPixel.B * 0.114));
                                    Color newPixel = Color.FromArgb(grayScale, grayScale, grayScale);

                                    bitmap.SetPixel(i, j, newPixel);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                });
                worker.Start();
                threads.Add(worker);
            }
            
            foreach(Thread th in threads)
            {
                th.Join();
            }

            return bmp;
        }

        static void Main(string[] args)
        {
            //ServerDivided();           
            ServerPool();

            //Zakljucak: Najbrza verzija je jednonitni server sa visenitnom obradom slike, nakon njega su (sa malom razlikom) visenitni server i visenitna obrada, 
            //a ubedljivo najsporiji je visenitni server sa jednonitnom obradom
        }

    }
}