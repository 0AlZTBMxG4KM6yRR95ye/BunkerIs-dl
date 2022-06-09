using BunkerIs_dl.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BunkerIs_dl
{
    public class Program
    {
        public const int DOWNLOAD_RETRIES = 5;
        public const string NEXT_DATA = "__NEXT_DATA__";
        public const string SAFE_CHARS = "-_.()";
        public const int THREAD_COUNT = 5;
        public const int TIMEOUT_DELAY_SECONDS = 10;
        private static readonly ManualResetEvent TimeoutHandler = new ManualResetEvent(true);

        /// <summary>
        /// Returns a string starting at the end of the specified substring
        /// </summary>
        /// <param name="toCut">The string to cut</param>
        /// <param name="cutAt">The substring to start at the end of </param>
        /// <param name="last">If true, starts at the last instance of the substring. default false</param>
        /// <returns></returns>
        public static string CutFrom(string toCut, string cutAt, bool last = false)
        {
            int index;
            if (!last)
            {
                index = toCut.IndexOf(cutAt);
            }
            else
            {
                index = toCut.LastIndexOf(cutAt);
            }

            index += cutAt.Length;

            return toCut.Substring(index);
        }

        /// <summary>
        /// Returns a string ending at the beginning of the specified substring
        /// </summary>
        /// <param name="toCut">The string to cut</param>
        /// <param name="cutAt">The substring to end at the beginning of </param>
        /// <returns></returns>
        public static string CutTo(string toCut, string cutAt)
        {
            int index = toCut.IndexOf(cutAt);

            return toCut.Substring(0, index);
        }

        /// <summary>
        /// This method takes a JSON defined file, and downloads it to the specified folder
        /// </summary>
        /// <param name="file"></param>
        /// <param name="localFolder"></param>
        public static async Task DownloadFile(AlbumFile file, string localFolder)
        {
            int retryCount = 0;
            //Saving this for later
            bool abort = false;

            WebClient wc = new WebClient();

            //Combine the CDN with the file name
            string fileUrl = file.Cdn + "/" + HttpEncode(file.Name);
            string localFilePath = Path.Combine(localFolder, file.Name);

            do
            {       
                //If we're here then we need a new file
                //So if anything exists, remove it
                if(File.Exists(localFilePath))
                {
                    File.Delete(localFilePath);
                }

                Log($"Downloading:\t{file.Name}\tSize:\t{file.Size:N0}");

                try
                {
                    //If we've timed out, wait for the event to expire before continuing.
                    _ = TimeoutHandler.WaitOne();

                    wc.DownloadFile(fileUrl, localFilePath);

                    //MP4 files return a landing page for a player, and not the file.
                    //Check to see if we got a landing page
                    string content_type = wc.ResponseHeaders["content-type"];

                    //If its a landing page
                    if (content_type.StartsWith("text/html"))
                    {
                        wc.Headers["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/102.0.5005.63 Safari/537.36";
                        wc.Headers["Referer"] = "https://stream.bunkr.is/";

                        //Extract the CDN server number from the url, and parse it
                        int serverNumber = int.Parse(Regex.Match(file.Cdn, "cdn(\\d*).bunkr.is").Groups[1].Value);

                        //Right now just assume this is the host.
                        //The CDN I tested with was #3, so its likely that the CDN number matches the bunker server.
                        string tryUrl = $"https://media-files{serverNumber}.bunkr.is/" + file.Name;

                        //Delete the HTML file we got last time
                        File.Delete(localFilePath);

                        //If we've timed out, wait for the event to expire before continuing.
                        _ = TimeoutHandler.WaitOne();

                        //Download the real file
                        wc.DownloadFile(tryUrl, localFilePath);
                    }

                    //Double check the downloaded file
                    if (!TestFile(file, localFilePath, out long? detectedFileLength))
                    {
                        if (detectedFileLength.HasValue)
                        {
                            //If its bad, throw an error
                            throw new System.IO.InvalidDataException($"Invalid file size detected. Expected {file.Size:N0}. Found {detectedFileLength.Value:N0}");
                        }
                        else
                        {
                            throw new System.IO.FileNotFoundException($"Downloaded file {file.Name} not found. How did we get here?");
                        }
                    }
                    else
                    {
                        //If its good, return
                        return;
                    }
                }
                //Bad file size
                catch (InvalidDataException idx)
                {
                    Log(idx.Message + ": " + file.Name);
                }
                //503 is a temporary unavailability.
                catch (Exception ex) when (ex.Message.Contains("(503)"))
                {
                    Log($"Server Unavailable (503). Waiting {TIMEOUT_DELAY_SECONDS} seconds: " + file.Name);

                    //First, lets block all threads from downloading in case we overloaded the server
                    _ = TimeoutHandler.Reset();

                    //Wait a few specified seconds
                    await Task.Delay(TIMEOUT_DELAY_SECONDS * 1000);

                    //Open the gates to allow the other threads to continue
                    _ = TimeoutHandler.Set();
                }
                //In the event of another error, we want to continue for the rest of the files, so catch and log it
                catch (Exception ex) when (!ex.Message.Contains("(503)"))
                {
                    Log($"An error has occurred downloading the file [{file.Name}]: " + ex.Message);

                    //We're not going to retry because we have no idea what went wrong
                    abort = true;
                }

                //If we're here something went wrong. Delete the file incase its corrupted.
                if (File.Exists(localFilePath))
                {
                    File.Delete(localFilePath);
                }

                //Increment our retry count
                retryCount++;

                //Have we exceeded the count for this file?
                if (retryCount > DOWNLOAD_RETRIES)
                {
                    //If so, return.
                    Log("Retry Count Exceeded. Aborting File Download: " + file.Name);
                    abort = true;
                }

                //If not, we're going to just fall through to the loop and continue
            } while (!abort);
        }

        /// <summary>
        /// Extracts the JSON object definition from the page so we can get the files
        /// </summary>
        /// <param name="pageSource"></param>
        /// <returns></returns>
        public static string ExtractJson(string pageSource)
        {
            pageSource = CutFrom(pageSource, NEXT_DATA);
            pageSource = CutFrom(pageSource, ">");
            return CutTo(pageSource, "<");
        }

        /// <summary>
        /// Http Encode hack to compensate for 4.8 missing the HttpUtility namespace
        /// </summary>
        /// <param name="inStr"></param>
        /// <returns></returns>
        public static string HttpEncode(string inStr)
        {
            StringBuilder outStr = new StringBuilder();

            foreach (char c in inStr)
            {
                if (!ShouldEncode(c)) //Dont encode standard values
                {
                    //Add unencoded character
                    _ = outStr.Append(c);
                }
                else
                {
                    //Add encoded hex string instead
                    _ = outStr.Append($"%{(int)c:X2}");
                }
            }

            return outStr.ToString();
        }

        public static async Task Main(string[] args)
        {
            //If no args, display help and exit
            if (!args.Any())
            {
                Log("Usage: BunkerIs-dl [Album1] [Album2] [Album3] ...");
                Log("Press any key to continue");
                _ = Console.ReadKey();
                return;
            }

            WebClient wc = new WebClient();

            //Loop through each provided album
            foreach (string album in args)
            {
                //We're going to store the files in a folder that matches the URL and not the album name,
                //because using the album name increases the likelyhood of an invalid character,
                //and I'm too lazy to account for that
                string folderName = CutFrom(album, "/", last: true);
                string localFolderName = Path.Combine(Directory.GetCurrentDirectory(), folderName);

                //Extract the data blob from the album page source
                string pageSource = wc.DownloadString(album);
                string json = ExtractJson(pageSource);
                PageData dataObject = Newtonsoft.Json.JsonConvert.DeserializeObject<PageData>(json);

                //Make sure we have somewhere to put the files
                if (!Directory.Exists(localFolderName))
                {
                    _ = Directory.CreateDirectory(localFolderName);
                }

                //Just referencing the files and logging the count
                List<AlbumFile> albumFiles = dataObject.Props.PageProps.Files;
                Log($"Found {albumFiles.Count} Files...");

                //Set up the thread safe download queue
                ConcurrentQueue<AlbumFile> fileQueue = new ConcurrentQueue<AlbumFile>();
                foreach (AlbumFile albumFile in albumFiles)
                {
                    string localFilePath = Path.Combine(localFolderName, albumFile.Name);
                    //Dont download existing valid files.
                    //We want to filter before we download so that we know the downloaded file
                    //debug text is the last to be logged to the window. Otherwise it looks like 
                    //All the files are completed if a file early on in the process needs to redownload

                    if (TestFile(albumFile, localFilePath, out long? existingFileLenth))
                    {
                        Log("Skipping existing file: " + albumFile.Name);
                        continue;
                    }
                    else if (existingFileLenth.HasValue)
                    //We found the file, so if the length has a value then the existing file was truncated for some reason
                    {
                        Log("Removing invalid existing file: " + albumFile.Name);
                    }

                    fileQueue.Enqueue(albumFile);
                }

                //Set up a place to hold our executing tasks
                Task[] Tasks = new Task[THREAD_COUNT];

                //Kick off the tasks
                for (int i = 0; i < THREAD_COUNT; i++)
                {
                    Tasks[i] = Task.Run(async () =>
                    {
                        //Just keep looping as long as the file bag has anything to give us
                        while (fileQueue.TryDequeue(out AlbumFile result))
                        {
                            await DownloadFile(result, localFolderName);
                        }

                        return Task.CompletedTask;
                    });                 
                }

                //Check and loop until we're ready to continue

                bool firstLine = true;

                do
                {
                    await Task.Delay(1000);

                    int waitingThreads = 0;

                    //Loop through all the tasks
                    for(int i = 0; i < THREAD_COUNT; i++)
                    {
                        if (!Tasks[i].IsCompleted)
                        {
                            waitingThreads++;
                        }
                    }

                    //If none are running, continue to the next album
                    if(waitingThreads == 0)
                    {
                        break;
                    }  else
                    {

                        //Its just a status update, so theres no point scrolling forever. 
                        //Just keep pasting over the current line, if we've already written an update
                        if(!firstLine)
                        {
                            Console.CursorLeft = 0;
                            Console.CursorTop = Console.CursorTop - 1;
                        }

                        //else log that we're waiting so we know we're not frozen
                        Log($"Waiting on {waitingThreads} threads.");
                        firstLine = false;
                    }
                } while (true);
            }
        }

        /// <summary>
        /// Log method ensures all text has a timestamp
        /// </summary>
        /// <param name="toLog"></param>
        public static void Log(string toLog)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {toLog}");
        }
        
        public static bool ShouldEncode(char c)
        {
            if (char.IsLetterOrDigit(c))
            {
                return false;
            }

            if (SAFE_CHARS.Contains(c))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validate the local file against the provided definition
        /// </summary>
        /// <param name="file">The provided AlbumFile definition</param>
        /// <param name="localFilePath">The local file path to test</param>
        /// <param name="delete">If true, delete invalid files. Default true</param>
        /// <returns>True if the file exists, and is the correct length</returns>
        public static bool TestFile(AlbumFile file, string localFilePath, bool delete = true)
        {
            return TestFile(file, localFilePath, out _, delete);
        }

        /// <summary>
        /// Validate the local file against the provided definition
        /// </summary>
        /// <param name="file">The provided AlbumFile definition</param>
        /// <param name="localFilePath">The local file path to test</param>
        /// <param name="detectedFileLength">If the file exists, this value holds the length</param>
        /// <param name="delete">If true, delete invalid files. Default true</param>
        /// <returns>True if the file exists, and is the correct length</returns>
        public static bool TestFile(AlbumFile file, string localFilePath, out long? detectedFileLength, bool delete = true)
        {
            detectedFileLength = null;

            if (!File.Exists(localFilePath))
            {
                return false;
            }

            detectedFileLength = new FileInfo(localFilePath).Length;

            bool success = detectedFileLength == file.Size;

            if (!success && delete)
            {
                File.Delete(localFilePath);
            }

            return success;
        }
    }
}