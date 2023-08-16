using _18203Proj2;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;

namespace _18203Proj1
{
    class Program
    {
        static HttpListener listener = new HttpListener();
        static LocalCache cache = new LocalCache();
        static Stopwatch stopwatch = new Stopwatch();
        static ReaderWriterLockSlim requestLock = new ReaderWriterLockSlim();
        static ReaderWriterLockSlim currentLock = new ReaderWriterLockSlim();

        public static async Task ServerAsync()
        {
            try
            {
                listener.Prefixes.Add("http://localhost:5050/");
                listener.Start();

                Console.WriteLine("Listening on port 5050...\n");

                string localPath = "C:\\Users\\krist\\sysprog\\18203Proj1\\photos\\";
                               
                cache.AddReq("http://localhost:5050/favicon.ico", new Bitmap($"{localPath}favicon.ico"));

                while (true)
                {
                    HttpListenerContext context = await  listener.GetContextAsync();
                    await ProcessRequestAsync(context, localPath);               
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        public static async Task ProcessRequestAsync(HttpListenerContext context, string localPath)
        {
            try
            {
                HttpListenerRequest request = context.Request;

                LogRequest(request);

                stopwatch.Restart();

                string ext = Path.GetExtension(request.RawUrl);

                if (request.Url == null)
                {
                    await NotFound(context);
                    return;
                }

                if (String.Compare(ext, ".jpg") != 0 &&
                    String.Compare(ext, ".jpeg") != 0 &&
                    String.Compare(ext, ".png") != 0 &&
                    String.Compare(ext, ".gif") != 0)
                {
                    await BadRequest(context);
                    return;
                }

                HttpListenerResponse response = context.Response;
                response.Headers.Set("Content-type", "image/jpg");

                Stream stream = response.OutputStream;
                Bitmap final;

                bool found = false;
                requestLock.EnterReadLock();
                found = cache.ContainReq(request.Url.ToString());
                requestLock.ExitReadLock();

                if (request.RawUrl == "/favicon.ico")
                {
                    final = new Bitmap($"{localPath}favicon.ico");
                }
                else if (found)
                {
                    Console.WriteLine($"Resource {request.RawUrl} found in cache\n");
                    requestLock.EnterReadLock();
                    cache.TryGetValue(request.Url.ToString(), out final);
                    requestLock.ExitReadLock();

                    if (final == null)
                    {
                        await NotFound(context);
                        return;
                    }
                }
                else
                {
                    Console.WriteLine($"Resource {request.RawUrl} not found in cache\n");

                    bool currentlyUsed;
                    currentLock.EnterReadLock();
                    currentlyUsed = cache.HasCurrent(request.Url.ToString());
                    currentLock.ExitReadLock();

                    if (!currentlyUsed)
                    {
                        currentLock.EnterWriteLock();
                        cache.AddCurrent(request.Url.ToString());
                        currentLock.ExitWriteLock();

                        Console.WriteLine($"Resource {request.RawUrl} taken. Processing...");

                        string path = localPath + request.Url.LocalPath;

                        if (File.Exists(path))
                        {
                            byte[] buffer = await File.ReadAllBytesAsync(path);
                        }
                        else
                        {
                            await NotFound(context);
                            Console.WriteLine($"{context.Request.RawUrl} not found");

                            currentLock.EnterWriteLock();
                            cache.RemoveCurrent(request.Url.ToString());
                            currentLock.ExitWriteLock();

                            return;
                        }

                        Bitmap imgFile = new Bitmap(path);

                        Bitmap[,] tiles = MakeTiles((object)imgFile);
                        Bitmap[,] res = ImageProcessTask(tiles);
                        final = JoinTiles(res, imgFile);

                        //prvo dodaje u kes obradjenih, a zatim uklanja iz kesa trenutnih
                        requestLock.EnterWriteLock();
                        cache.AddReq(request.Url.ToString(), final);
                        requestLock.ExitWriteLock();

                        currentLock.EnterWriteLock();
                        cache.RemoveCurrent(request.Url.ToString());
                        currentLock.ExitWriteLock();

                        Console.WriteLine($"{request.RawUrl} ready.");
                    }
                    else
                    {
                        Console.WriteLine($"{request.RawUrl} is currently being processed. Waiting...");
                                                
                        while (currentlyUsed) //spinning
                        {
                            currentLock.EnterReadLock();
                            currentlyUsed = cache.HasCurrent(request.Url.ToString());
                            currentLock.ExitReadLock();
                        }

                        Console.WriteLine($"{request.RawUrl} ready.");

                        requestLock.EnterReadLock();
                        cache.TryGetValue(request.Url.ToString(), out final);
                        requestLock.ExitReadLock();

                        if (final == null)
                        {
                            NotFound(context);
                            return;
                        }
                    }
                }

                byte[] resArray = ToByteArr(final, System.Drawing.Imaging.ImageFormat.Bmp);
                response.ContentLength64 = resArray.Length;

                await stream.WriteAsync(resArray, 0, resArray.Length);

                stopwatch.Stop();

                LogResponse(response, request.RawUrl);
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());

                HttpListenerResponse response = context.Response;
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.ContentType = "text/plain";
                string errorMessage = "An error occurred while processing the request.";
                byte[] errorBytes = Encoding.UTF8.GetBytes(errorMessage);
                response.ContentLength64 = errorBytes.Length;
                await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);

                //probno mesto
                requestLock.ExitWriteLock();
                currentLock.ExitWriteLock();
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
        public static async Task NotFound(HttpListenerContext context)
        {
            HttpListenerResponse res = context.Response;

            res.Headers.Set("Content-Type", "text/plain");
            res.StatusCode = (int)HttpStatusCode.NotFound;

            Stream stream = res.OutputStream;

            string err = $"404- {context.Request.RawUrl} not found";
            byte[] ebuf = Encoding.UTF8.GetBytes(err);
            res.ContentLength64 = ebuf.Length;

            await stream.WriteAsync(ebuf, 0, ebuf.Length);

            requestLock.EnterWriteLock();
            cache.AddReq(context.Request.Url.ToString(), null);
            requestLock.ExitWriteLock();
        }

        public static async Task BadRequest(HttpListenerContext context)
        {
            HttpListenerResponse res = context.Response;
            res.StatusCode = (int)HttpStatusCode.BadRequest;

            Stream stream = res.OutputStream;

            string err = $"500- Internal error. Check the file extension";
            byte[] ebuf = Encoding.UTF8.GetBytes(err);
            res.ContentLength64 = ebuf.Length;

            await stream.WriteAsync(ebuf, 0, ebuf.Length);
        }
        #endregion

        public static byte[] ToByteArr(Bitmap bitmap, System.Drawing.Imaging.ImageFormat format)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                lock (bitmap)
                {
                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                    return ms.ToArray();
                }
            }
        }

        #region imageManipulation
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

        public static Bitmap[,] ImageProcessTask(Bitmap[,] bmp)
        {
            int numRows = bmp.GetLength(0);
            int numCols = bmp.GetLength(1);
            int numTasks = numRows * numCols;

            Task[] tasks = new Task[numTasks];
            int tskNum = 0;

            foreach(Bitmap bitmap in bmp) { 

                    tasks[tskNum++] = Task.Run(() =>
                    {
                        lock (bitmap)
                        {

                            int width = bitmap.Width;
                            int height = bitmap.Height;

                            for (int x = 0; x < width; x++)
                            {
                                for (int y = 0; y < height; y++)
                                {
                                    Color oldPixel = bitmap.GetPixel(x, y);

                                    int grayScale = (int)((oldPixel.R * 0.229) + (oldPixel.G * 0.587) + (oldPixel.B * 0.114));
                                    Color newPixel = Color.FromArgb(grayScale, grayScale, grayScale);

                                    bitmap.SetPixel(x, y, newPixel);
                                }
                            }
                        }
                    });
            }

            Task.WaitAll(tasks);
            return bmp;
        }

        #region threadedVersions

        //singlethreaded server + multithreaded picture processing
        public static void ServerDivided()
        {
            listener.Prefixes.Add("http://localhost:5050/");
            listener.Start();

            Console.WriteLine("Listening on port 5050...\n");

            string localPath = "C:\\Users\\krist\\sysprog\\18203Proj1\\photos\\";
            cache.AddReq("http://localhost:5050/favicon.ico", new Bitmap($"{localPath}favicon.ico"));

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

                    if (cache.ContainReq(request.Url.ToString()))
                    {
                        Console.WriteLine($"Resource {request.RawUrl} found in cache\n");
                        cache.TryGetValue(request.Url.ToString(), out final);

                        if (final == null)
                        {
                            NotFound(context);
                            return;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Resource {request.RawUrl} not found in cache\n");
                        string path = localPath + request.Url.LocalPath;

                        if (File.Exists(path))
                        {
                            byte[] buffer = File.ReadAllBytes(path);
                        }
                        else
                        {
                            NotFound(context);
                            Console.WriteLine($"{context.Request.RawUrl} not found");
                            return;
                        }

                        Bitmap imgFile = new Bitmap(path);
                        Bitmap[,] tiles = MakeTiles((object)imgFile);
                        Bitmap[,] res = ParallelImageProcess(tiles);
                        final = JoinTiles(res, imgFile);

                        cache.AddReq(request.Url.ToString(), final);
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

        //multithreaded server + 2 way picture processing
        public static void ServerPool() //perfected
        {
            listener.Prefixes.Add("http://localhost:5050/");
            listener.Start();

            Console.WriteLine("Listening on port 5050...\n");

            string localPath = "C:\\Users\\krist\\sysprog\\18203Proj1\\photos\\";
            cache.AddReq("http://localhost:5050/favicon.ico", new Bitmap($"{localPath}favicon.ico"));
            int BR = 0;

            while (true)
            {
                ThreadPool.QueueUserWorkItem((state) =>
                {
                    try
                    {
                        HttpListenerContext context = listener.GetContextAsync().Result;
                        HttpListenerRequest request = context.Request;

                        LogRequest(request);
                        Thread.CurrentThread.Name = $"Thread{++BR}";
                        stopwatch.Restart();

                        HttpListenerResponse response = context.Response;
                        response.Headers.Set("Content-type", "image/jpg");
                        Stream stream = response.OutputStream;
                        Bitmap final;

                        if (request.Url == null)
                        {
                            throw new HttpRequestException();
                        }

                        bool found = false;

                        requestLock.EnterReadLock();
                        found = cache.ContainReq(request.Url.ToString());
                        requestLock.ExitReadLock();

                        if (found)
                        {
                            Console.WriteLine($"Request for {request.RawUrl} FOUND in cache\n");

                            requestLock.EnterReadLock();
                            cache.TryGetValue(request.Url.ToString(), out final);
                            requestLock.ExitReadLock();

                            if (final == null)
                            {
                                NotFound(context);
                                return;
                            }
                        }
                        else if (request.RawUrl == "/favicon.ico")
                        {
                            Bitmap fav = new Bitmap($"{localPath}favicon.ico");
                            requestLock.EnterWriteLock();
                            cache.AddReq("http://localhost:5050/favicon.ico", fav);
                            requestLock.ExitWriteLock();

                            final = fav;
                        }
                        else
                        {
                            Console.WriteLine($"Resource {request.RawUrl} NOT FOUND in cache\n");

                            //ako nije nadjen, proveriti da li je u current
                            bool currentlyUsed = false;

                            currentLock.EnterReadLock();
                            currentlyUsed = cache.HasCurrent(request.Url.ToString());
                            currentLock.ExitReadLock();

                            //ako nije u current, upisati ga i pokrenuti obradu
                            if (!currentlyUsed)
                            {
                                currentLock.EnterWriteLock();
                                cache.AddCurrent(request.Url.ToString());
                                currentLock.ExitWriteLock();

                                Console.WriteLine($"Resource {request.RawUrl} taken by {Thread.CurrentThread.Name}. Processing...");

                                string basep = System.AppDomain.CurrentDomain.BaseDirectory;
                                string localPath = $"{basep.Remove(34)}\\photos";

                                string path = localPath + request.Url.LocalPath;

                                if (File.Exists(path))
                                {
                                    byte[] buffer = File.ReadAllBytes(path);
                                }
                                else
                                {
                                    NotFound(context);
                                    Console.WriteLine($"{context.Request.RawUrl} not found");

                                    currentLock.EnterWriteLock();
                                    cache.RemoveCurrent(request.Url.ToString());
                                    currentLock.ExitWriteLock();

                                    return;
                                }

                                Bitmap imgFile = new Bitmap(path);

                                Bitmap[,] tiles = MakeTiles((object)imgFile);
                                Bitmap[,] res = MultithreadedImageProcess(tiles);
                                final = JoinTiles(res, imgFile);
                                
                                //prvo dodaje u kes obradjenih, a zatim uklanja iz kesa trenutnih
                                requestLock.EnterWriteLock();
                                cache.AddReq(request.Url.ToString(), final);
                                requestLock.ExitWriteLock();

                                currentLock.EnterWriteLock();
                                cache.RemoveCurrent(request.Url.ToString());
                                currentLock.ExitWriteLock();

                                Console.WriteLine($"{request.RawUrl} ready.");
                            }
                            else
                            {
                                Console.WriteLine($"{request.RawUrl} is currently being processed. Waiting...");
                                while (currentlyUsed) //spinning
                                {
                                    currentLock.EnterReadLock();
                                    currentlyUsed = cache.HasCurrent(request.Url.ToString());
                                    currentLock.ExitReadLock();
                                }

                                Console.WriteLine($"{request.RawUrl} ready.");
                                                                
                                requestLock.EnterReadLock();
                                cache.TryGetValue(request.Url.ToString(), out final);
                                requestLock.ExitReadLock();

                                if (final == null)
                                {
                                    NotFound(context);
                                    return;
                                }
                            }
                        }

                        //returning by stream
                        byte[] resArray = ToByteArr(final, System.Drawing.Imaging.ImageFormat.Bmp);
                        response.ContentLength64 = resArray.Length;
                        stream.Write(resArray, 0, resArray.Length);

                        stopwatch.Stop();

                        LogResponse(response, request.RawUrl);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                });
            }
        }

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

            foreach (Thread th in threads)
            {
                th.Join();
            }

            return bmp;
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
                    catch (Exception ex)
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
        #endregion

        static async Task Main(string[] args)
        {
            //ServerDivided();           
            //ServerPool();
            Task mainTask = Task.Run(() => ServerAsync());
            mainTask.Wait();
        }

    }
}