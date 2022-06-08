using BunkerIs_dl.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace BunkerIs_dl
{
    public class Program
    {
        public const string NEXT_DATA = "__NEXT_DATA__";
        public const int THREAD_COUNT = 5;

        /// <summary>
        /// This method takes a JSON defined file, and downloads it to the specified folder
        /// </summary>
        /// <param name="file"></param>
        /// <param name="localFolder"></param>
        public static void DownloadFile(AlbumFile file, string localFolder)
        {
            WebClient wc = new WebClient();

            //Combine the CDN with the file name
            string fileUrl = file.Cdn + "/" + HttpUtility.UrlEncode(file.Name);

            string localFilePath = Path.Combine(localFolder, file.Name);

            //Dont download existing files. 
            if (File.Exists(localFilePath))
            {
                Console.WriteLine("Skipping existing file: " + localFilePath);
                return;
            }

            Console.WriteLine("Downloading: " + file.Name);

            try
            {
                wc.DownloadFile(fileUrl, localFilePath);


                //MP4 files return a landing page for a player, and not the file.
                //Check to see if we got a landing page
                string content_type = wc.ResponseHeaders["content-type"];

                //If its a landing page
                if (content_type.StartsWith("text/html"))
                {
                    //Right now just assume this is the host. 
                    //The CDN I tested with was #3, so its likely that the CDN number matches the bunker server.
                    //If a non-3 CDN is found, let me know so I can account for it
                    string tryUrl = "https://media-files3.bunkr.is/" + file.Name;

                    //Delete the HTML file we got last time
                    File.Delete(localFilePath);

                    //Download the real file
                    wc.DownloadFile(tryUrl, localFilePath);
                }
            }

            //In the event of an error, we want to continue for the rest of the files, so catch and log it
            catch (Exception ex)
            {
                //Delete any file stubs that might be left over from the failure
                if (File.Exists(localFilePath))
                {
                    File.Delete(localFilePath);
                }

                Console.WriteLine($"An error has occurred downloading the file [{file.Name}]: " + ex.Message);
            }
        }

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

        public static void Main(string[] args)
        {
            //If no args, display help and exit
            if (!args.Any())
            {
                Console.WriteLine("Usage: BunkerIs-dl [Album1] [Album2] [Album3] ...");
                Console.WriteLine("Press any key to continue");
                _ = Console.ReadKey();
                return;
            }

#pragma warning disable SYSLIB0014 // Type or member is obsolete
            WebClient wc = new WebClient();
#pragma warning restore SYSLIB0014 // Type or member is obsolete

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
                Console.WriteLine($"Found {albumFiles.Count} Files...");

                //Could use a for/foreach loop here, but this is how you write a multithreaded loop
                //easily in c#. It executes on the number of threads provided by the constant, and for each file it calls the DownloadFile method
                _ = Parallel.ForEach(albumFiles, 
                    new ParallelOptions()
                    {
                        MaxDegreeOfParallelism = THREAD_COUNT
                    } , 
                    file => DownloadFile(file, localFolderName)
                );
            }
        }
    }
}