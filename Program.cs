/*==============================================================================
 * (C) Copyright 2017,2022,2023,2024 John J Kauflin, All rights reserved.
 *----------------------------------------------------------------------------
 * DESCRIPTION:  Server-side Controller to handle websocket requests with
 *               a specified task to execute
 *               Server-side code to execute tasks and log text to a display
 *               using a websocket connection
 *----------------------------------------------------------------------------
 * Modification History
 * 2017-09-08 JJK 	Initial version
 * 2017-12-29 JJK	Initial controls and WebSocket communication
 * 2022-04-17 JJK   Making updates for bootstrap 5, and to use fetch()
 *                  instead of AJAX.  Removed old websocket stuff
 * 2022-05-31 JJK   Updated to use newest fetch ideas for lookup and update,
 *                  and converted to use straight javascript
 * 2022-10-20 JJK   Re-implemented websocket connection to display async log
 * 2022-12-17 JJK   Re-implemented using .NET 6 C# backend server instead of
 *                  nodejs.  Got User Secrets, Configuration injection, 
 *                  and connection string for remote MySQL working
 * 2022-12-20 JJK   Implemented NLog logger and added to dependency injection
 *----------------------------------------------------------------------------
 * 2019-02-11 JJK   Initial version
 * 2020-07-07 JJK   Modified to work with new MediaGallery and createThumbnails which takes "subPath" as a parameter
 * 2021-05-09 JJK   Re-factored for MediaGallery-Admin. Working on FTP functions
 * 2021-05-27 JJK   Re-worked the file loop to get list of only image files
 * 2021-05-28 JJK   Completed new FTP and create thumbnail logic
 * 2021-07-03 JJK   Added logic to create the remote directory if missing
 * 2021-10-30 JJK   Modified to save a last completed timestamp and look for files with a timestamp greater than last run
 * 2022-10-20 JJK   Re-implemented websocket connection to display async log
 * 2022-12-17 JJK   Re-implemented using .NET 6 C# backend server instead of nodejs
 * 2022-12-18 JJK   (Argentina beats France to win world cup)  Implemented
 *                  recursive walk through of directories and verified the
 *                  recursive "queue" completes before the first call returns
 *                  (unlike nodejs)
 * 2022-12-19 JJK   Got MySQL queries to work on ConfigParam
 * 2022-12-20 JJK   Got FTP functions, and LastRunDate parameter update working
 * 2022-12-21 JJK   Got https GET working for CreateThumbnail call
 * 2022-12-22 JJK   Moved the execution tasks into the Controller (to take 
 *                  advantage of the injected logger)
 * 2022-12-23 JJK   Working on update file info (to new DB table)
 * 2022-12-24 JJK   Implemented a Dictionary to hold config keys and values
 * 2022-12-27 JJK   Implemented micro-ORM to do database work (see Model)
 * 2022-12-29 JJK   Implemented final max retry checks
 * 2023-01-01 JJK   Implemented MetadataExtractor to data from photos, and
 *                  a special binary read to get the Picasa face people string
 * 2023-01-06 JJK   Implemented RegEx pattern to get DateTime from filename
 * 2023-01-11 JJK   Implemented ExifLibrary to get and set metadata values
 * 2023-01-20 JJK   Implemented MediaType and MediaCategory concepts
 * 2023-02-05 JJK   Updated to Bootstrap v5.2 and newest nav and menu ideas
 *                  Starting to work on new file edit processing
 * 2023-02-18 JJK   Gave up on doing the Edit stuff here (putting Admin
 *                  functions in MediaGallery web package)
 *                  Updated to set values in new People and Album tables
 * 2023-02-24 JJK   Modified to do file transfer for new files, including
 *                  calculation of date taken, update of photo metadata,
 *                  FTP of file, and call to create thumbnail
 * 2023-08-18 JJK   Uncommented Update File Info to work on fixes like
 *                  bad paths
 * 2024-01-14 JJK   Implement a screen up upload a music album
 * ---------------------------------------------------------------------------
 * 2024-03-03 JJK   Implemented upload of smaller images to Azure blob storage
 * 2024-03-10 JJK   Implemented update of Azure CosmosDB NoSQL containers
 *                  for storing media gallery information
 * 2024-03-13 JJK   Working on image resize for thumbnails and smaller
 * 2024-03-15 JJK   Switched to Azure Table for media info storage (*** NOPE
 * 2024-04-09 JJK   Tests of the Azure web site storage, metadata query, and 
 *                  performance have worked out ok, so modifying this 
 *                  console to be the general upload - resize images for
 *                  smaller and thumbnail into blob storage, and update of
 *                  Cosmos DB NoSQL metadata entities.
 * 2024-04-15 JJK   Moved Album and People data to Cosmos DB entities
 * 2024-04-17 JJK   Implementing an upload for music albums
 * 2024-10-22 JJK   Implemented override to use filename for date+time
 *                  instead of the file metadata
 * 2024-10-28 JJK   Modified to use minutes and seconds from iOS filename
 *                  for taken value, and updated Azure Cosmos DB update for
 *                  existing names to fetch and delete the old first
 *============================================================================*/
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using System.Net;
using System.Text.RegularExpressions;

using MediaGalleryConsole.Model;
using ExifLibrary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Csv;
using Newtonsoft.Json.Linq;
using System.IO;
using Azure;

namespace MediaGalleryConsole
{
    class Program
    {
        private static List<DatePattern> dpList = new List<DatePattern>();
        private static DateTime minDateTime = new DateTime(1800, 1, 1);
        private static string author = "John J Kauflin";

        private static string? jjkwebStorageConnStr;
        private static string? jjkWebNoSqlUri;
        private static string? jjkWebNoSqlKey;
        private static readonly Stopwatch timer = new Stopwatch();
        private static DateTime lastRunDate;
        //private static ArrayList fileList = new ArrayList();

        private CosmosClient cosmosClient;
        private Database database;
        private Container container;
        private string databaseId = "JJKWebDB";
        private string containerId = "MediaInfo";

        // <Main>
        public static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("Beginning operations...\n");
                timer.Start();

                // Get configuration parameters from the Secrets JSON (not checked into source code control)
                IConfigurationRoot config = new ConfigurationBuilder()
                    .AddUserSecrets<Program>()
                    .Build();
                jjkwebStorageConnStr = config["jjkwebStorageConnStr"];
                jjkWebNoSqlUri = config["JJKWebNoSqlUri"];
                jjkWebNoSqlKey = config["JJKWebNoSqlKey"];

                loadDatePatterns();

                // Call an asynchronous method to start the processing
                Program p = new Program();
                //await p.ProcessMusicAsync();
                //await p.MoveDataAsync();
                //await p.PurgeMetricsAsync();
                await p.ProcessPhotosAsync();
            }
            catch (CosmosException de)
            {
                Exception baseException = de.GetBaseException();
                Console.WriteLine("{0} error occurred: {1}", de.StatusCode, de);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e);
            }
            finally
            {
                timer.Stop();
                Console.WriteLine($"END elapsed time = {timer.Elapsed.ToString()}");
            }
        }

        // <ProcessPhotosAsync>
        /// <summary>
        /// Entry point to start processing
        /// </summary>
        public async Task ProcessPhotosAsync()
        {
            int mediaTypeId = 1;    // Photos
            //int mediaTypeId = 2;  // Videos
            //int mediaTypeId = 3;  // Music
            FileInfo fi;
            string category;
            string menu;
            var defaultDate = DateTime.Parse("01/01/1800");
            DateTime takenDT = defaultDate;
            string rootPath = "D:/Projects/johnkauflin/public_html/home/Media/Photos";
            lastRunDate = DateTime.Parse("11/13/2024 00:00:00");

            // Create a new instance of the Cosmos Client

            cosmosClient = new CosmosClient(jjkWebNoSqlUri, jjkWebNoSqlKey,
                new CosmosClientOptions()
                {
                    ApplicationName = "MediaGalleryConsole"
                }
            );

            database = cosmosClient.GetDatabase(databaseId);
            container = cosmosClient.GetContainer(databaseId, containerId);
            var photosContainer = new BlobContainerClient(jjkwebStorageConnStr, "photos");
            var thumbsContainer = new BlobContainerClient(jjkwebStorageConnStr, "thumbs");

            Console.WriteLine($"Last Run = {lastRunDate.ToString("MM/dd/yyyy HH:mm:ss")}");

            //bool storageOverwrite = true;
            bool storageOverwrite = false;
            string ext;
            int index = 0;
            foreach (string filePath in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories))
            {
                fi = new FileInfo(filePath);
                //if (fi.LastWriteTime < lastRunDate)
                if (fi.CreationTime < lastRunDate)
                {
                        continue;
                }

                // Skip files in this directory
                if (fi.FullName.Contains(".picasaoriginals") || fi.Name.Equals("1987-01 001.jpg"))
                {
                    continue;
                }
                /*
                if (!fi.Name.StartsWith("20241012_170906790"))
                {
                    continue;
                }
                */

                index++;
                /*
                if (index < 6000)
                {
                    continue;
                }
                */

                ext = fi.Extension.ToLower();
                if (!ext.Equals(".jpeg") && !ext.Equals(".jpg") && !ext.Equals(".png") && !ext.Equals(".gif"))
                {
                    continue;
                }

                // Set a few fields in the image metadata, and get a good taken datetime
                takenDT = setPhotoMetadata(fi);
                if (takenDT.Year == 1)
                {
                    takenDT = defaultDate;
                }

                // Get the category and menu from the file path
                var dirParts = fi.FullName.Substring(rootPath.Length + 1).Replace(@"\", @"/").Split('/');
                category = dirParts[0];
                menu = dirParts[1];

                Console.WriteLine($"{index}, {fi.Name}, taken = {takenDT}, {category}, {menu} ");

                // Load the image, create resized images and upload to the blob storage containers
                using SixLabors.ImageSharp.Image image = SixLabors.ImageSharp.Image.Load(fi.FullName);
                await UploadImgToStorageAsync(photosContainer, fi, image, 2000, storageOverwrite);
                await UploadImgToStorageAsync(thumbsContainer, fi, image, 110, storageOverwrite);

                // Create a metadata object from the media file information
                MediaInfo mediaInfo = new MediaInfo
                {
                    id = Guid.NewGuid().ToString(),
                    MediaTypeId = mediaTypeId,
                    Name = fi.Name,
                    TakenDateTime = takenDT,
                    //TakenFileTime = takenDT.ToFileTime(),
                    TakenFileTime = int.Parse(takenDT.ToString("yyyyMMddHH")),
                    CategoryTags = category,
                    MenuTags = menu,
                    AlbumTags = "",
                    Title = "",
                    Description = "",
                    People = "",
                    ToBeProcessed = false,
                    SearchStr = fi.Name.ToLower()
                };

                if (mediaInfo.CategoryTags.Length == 0 && mediaInfo.MenuTags.Length == 0)
                {
                    mediaInfo.ToBeProcessed = true;
                }

                //Console.WriteLine(mediaInfo);
                try
                {
                    await container.CreateItemAsync<MediaInfo>(mediaInfo, new PartitionKey(mediaInfo.MediaTypeId));
                }
                catch (CosmosException cex) when (cex.StatusCode == HttpStatusCode.Conflict)
                {
                    // Ignore duplicate error, just continue on
                    /*
                    Console.WriteLine($"Conflict with Create on Name (duplicate): {mediaInfo.Name} ");

                    // Delete all previous documents with the filename and insert a brand new document with updated values
                    // c.Name = "20241012_170906790_iOS.jpg"
                    var queryText = $"SELECT * FROM c WHERE c.Name = \"{mediaInfo.Name}\" ";
                    var feed = container.GetItemQueryIterator<MediaInfo>(queryText);
                    while (feed.HasMoreResults)
                    {
                        var response = await feed.ReadNextAsync();
                        foreach (var item in response)
                        {
                            //metricData.kWh_bucket_YEAR = float.Parse(item.TotalValue);
                            container.DeleteItemAsync<MediaInfo>(item.id, new PartitionKey(mediaInfo.MediaTypeId));
                        }
                    }
                    await container.CreateItemAsync<MediaInfo>(mediaInfo, new PartitionKey(mediaInfo.MediaTypeId));
                    */
                }
                catch (Exception ex)
                {
                    // Log any other exceptions and stop
                    Console.WriteLine(ex.Message);
                    throw ex;
                }

            } // File loop

            if (index == 0)
            {
                Console.WriteLine("No new files found");
            }

            Console.WriteLine("");
            Console.WriteLine(">>>>> Don't forget to update Last Run Time");

        } // public async Task ProcessPhotosAsync()


        public async Task PurgeMetricsAsync()
        {
            Console.WriteLine($"Purging MetricPoint data older than 3 days ");

            var jjkCosmosClient = new CosmosClient(jjkWebNoSqlUri, jjkWebNoSqlKey,
                new CosmosClientOptions()
                {
                    ApplicationName = "MediaGalleryConsole"
                }
            );

            var databaseNEW = jjkCosmosClient.GetDatabase("JJKWebDB");
            var containerNEW = jjkCosmosClient.GetContainer("JJKWebDB", "MetricPoint");

            DateTime currDateTime = DateTime.Now;

            try
            {
                //metricPointContainer.CreateItemAsync<MetricPoint>(metricPoint, new PartitionKey(metricPoint.PointDay));

                string maxYearMonthDay = currDateTime.AddDays(-3).ToString("yyyyMMdd");
                int cnt = 0;
                // currDateTime - 3 days
                //int dayVal = int.Parse(metricData.metricDateTime.ToString("yyyyMMdd"));  // 
                var queryText = $"SELECT * FROM c WHERE c.PointDay < {maxYearMonthDay} ";
                var feed = containerNEW.GetItemQueryIterator<MetricPoint>(queryText);
                while (feed.HasMoreResults)
                {
                    var response = await feed.ReadNextAsync();
                    foreach (var item in response)
                    {
                        cnt++;
                        // execute a delete item on each document
                        Console.WriteLine($"{cnt} {item.id} {item.PointDateTime} ");
                        await containerNEW.DeleteItemAsync<MetricPoint>(item.id, new PartitionKey(item.PointDay));
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw ex;
            }

        } // PurgeMetricsAsync

        public async Task MoveDataAsync()
        {
            //Console.WriteLine($"Moving data to new jjkwebnosql ");
            Console.WriteLine($"Moving data from MediaInfo to MediaInfoTEMP (with a Unique Key on /MediaTypeId,/Name ");

            var cosmosClient = new CosmosClient(jjkWebNoSqlUri, jjkWebNoSqlKey,
                new CosmosClientOptions()
                {
                    ApplicationName = "MediaGalleryConsole"
                }
            );

            database = cosmosClient.GetDatabase(databaseId);
            container = cosmosClient.GetContainer(databaseId, "MediaInfoTEMP");

            var containerNEW = cosmosClient.GetContainer(databaseId, containerId);

            // Get the existing document from Cosmos DB
            //var queryText = $"SELECT * FROM c WHERE c.PointDay > 20240723 ";
            var queryText = $"SELECT * FROM c ";
            var feed = container.GetItemQueryIterator<MediaInfo>(queryText);
            int cnt = 0;
            while (feed.HasMoreResults)
            {
                var response = await feed.ReadNextAsync();
                foreach (var item in response)
                {
                    cnt++;

                    try
                    {
                        await containerNEW.CreateItemAsync<MediaInfo>(item, new PartitionKey(item.MediaTypeId));
                        Console.WriteLine($"{cnt} Created Item ");
                    }
                    catch (CosmosException cex) when (cex.StatusCode == HttpStatusCode.Conflict)
                    {
                        // Ignore duplicate error, just continue on
                        Console.WriteLine($"Conflict with Create on Name (duplicate): {item.Name} ");
                    }
                    catch (Exception ex)
                    {
                        // Log any other exceptions and stop
                        Console.WriteLine(ex.Message);
                        throw ex;
                    }

                }
            }

        } // public async Task MoveDataAsync()


        // <ProcessPhotosAsync>
        /// <summary>
        /// Entry point to start processing
        /// </summary>
        public async Task ProcessMusicAsync()
        {
            int mediaTypeId = 3;  // Music
            FileInfo fi;
            var defaultDate = DateTime.Parse("01/01/1800");
            DateTime takenDT = defaultDate;
            string rootPath = "D:/Projects/johnkauflin/public_html/home/Media/Music";
            //lastRunDate = DateTime.Parse("01/01/1800 00:00:00");
            lastRunDate = DateTime.Parse("05/29/2024 10:40:00");

            // Create a new instance of the Cosmos Client
            cosmosClient = new CosmosClient(jjkWebNoSqlUri, jjkWebNoSqlKey,
                new CosmosClientOptions()
                {
                    ApplicationName = "MediaGalleryConsole"
                }
            );

            database = cosmosClient.GetDatabase(databaseId);
            container = cosmosClient.GetContainer(databaseId, containerId);
            var musicContainer = new BlobContainerClient(jjkwebStorageConnStr, "music");

            Console.WriteLine($"Last Run = {lastRunDate.ToString("MM/dd/yyyy HH:mm:ss")}");

            //bool storageOverwrite = true;
            bool storageOverwrite = false;
            string idStr;
            string band;
            string album;
            string storageFilename;
            string ext;
            int index = 0;
            foreach (string filePath in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories))
            {
                fi = new FileInfo(filePath);
                if (fi.LastWriteTime < lastRunDate)
                {
                    continue;
                }

                ext = fi.Extension.ToLower();
                if (!ext.Equals(".mp3"))
                {
                    continue;
                }

                index++;
                /*
                if (index < 6000)
                {
                    continue;
                }
                */

                // Set a few fields in the image metadata, and get a good taken datetime
                takenDT = fi.CreationTime;
                if (takenDT.Year == 1)
                {
                    takenDT = defaultDate;
                }

                // Get the category and menu from the file path
                var dirParts = fi.FullName.Substring(rootPath.Length + 1).Replace(@"\", @"/").Split('/');
                band = dirParts[0];     // CategoryTags
                album = dirParts[1];    // MenuTags
                storageFilename = band + " " + album + " " + fi.Name;
                takenDT = getDateFromFilename(fi.FullName);

                Console.WriteLine($"{index}, {band} {album}, {fi.Name}, taken = {takenDT} ");

                // Upload the file to music blob storage
                var blobClient = musicContainer.GetBlobClient(storageFilename);
                if (!blobClient.Exists())
                {
                    //blobClient.Upload(memoryStream, storageOverwrite);
                    blobClient.Upload(fi.FullName);
                }

                // Query the container and get the ID if the Name already exists
                idStr = Guid.NewGuid().ToString();
                var queryText = $"SELECT * FROM c WHERE c.Name = \"{storageFilename}\" ";
                var feed = container.GetItemQueryIterator<MediaInfo>(queryText);

                while (feed.HasMoreResults)
                {
                    var response = await feed.ReadNextAsync();
                    foreach (var item in response)
                    {
                        //Console.WriteLine($"Found item:\t{item.Name}");
                        idStr = item.id;
                    }
                }

                // Create a metadata object from the media file information
                MediaInfo mediaInfo = new MediaInfo
                {
                    id = idStr,
                    MediaTypeId = mediaTypeId,
                    Name = storageFilename,
                    TakenDateTime = takenDT,
                    //TakenFileTime = takenDT.ToFileTime(),
                    TakenFileTime = int.Parse(takenDT.ToString("yyyyMMddHH")),
                    CategoryTags = band,
                    MenuTags = album,
                    AlbumTags = "",
                    Title = fi.Name,
                    Description = "",
                    People = "",
                    ToBeProcessed = false,
                    SearchStr = storageFilename
                };


                if (mediaInfo.CategoryTags.Length == 0 && mediaInfo.MenuTags.Length == 0)
                {
                    mediaInfo.ToBeProcessed = true;
                }

                //Console.WriteLine(mediaInfo);
                try
                {
                    //await container.CreateItemAsync<MediaInfo>(mediaInfo, new Microsoft.Azure.Cosmos.PartitionKey(mediaInfo.MediaTypeId));
                    await container.UpsertItemAsync<MediaInfo>(mediaInfo, new Microsoft.Azure.Cosmos.PartitionKey(mediaInfo.MediaTypeId));
                }
                catch (CosmosException cex) when (cex.StatusCode == HttpStatusCode.Conflict)
                {
                    // Ignore duplicate error, just continue on
                    //Console.WriteLine($"Conflict with Create on Name (duplicate): {mediaInfo.Name} ");
                }
                catch (Exception ex)
                {
                    // Log any other exceptions and stop
                    Console.WriteLine(ex.Message);
                    throw ex;
                }

            } // File loop

            if (index == 0)
            {
                Console.WriteLine("No new files found");
            }

            Console.WriteLine("");
            Console.WriteLine(">>>>> Don't forget to update Last Run Time");

        } // public async Task ProcessMusicAsync()




        private async Task UploadImgToStorageAsync(BlobContainerClient containerClient, FileInfo fi, SixLabors.ImageSharp.Image image, int desiredImgSize, bool storageOverwrite)
        {
            var blobClient = containerClient.GetBlobClient(fi.Name);
            if (blobClient.Exists() && !storageOverwrite)
            {
                // Files already exist (and we don't want to overwrite)
                return;
            }

            // If you pass 0 as any of the values for width and height dimensions then ImageSharp will
            // automatically determine the correct opposite dimensions size to preserve the original aspect ratio.
            //thumbnails just make img.height = 110   (used to use 130)

            int newImgSize = desiredImgSize;
            if (newImgSize > Math.Max(image.Width, image.Height))
            {
                newImgSize = Math.Max(image.Width, image.Height);
            }

            int width = image.Width;
            int height = image.Height;

            if (desiredImgSize < 200)
            {
                width = 0;
                height = newImgSize;
            }
            else
            {
                if (width > height)
                {
                    width = newImgSize;
                    height = 0;
                }
                else
                {
                    width = 0;
                    height = newImgSize;
                }
            }

            image.Mutate(x => x.Resize(width, height));
            MemoryStream memoryStream = new MemoryStream();
            image.Save(memoryStream, image.Metadata.DecodedImageFormat);
            memoryStream.Position = 0;
            blobClient.Upload(memoryStream, storageOverwrite);

            // This is how to save it to a file instead of a stream
            //var outPath = "C:/Users/johnk/Downloads/smaller/" + fi.Name;
            //image.Save(outPath);
        }
        // </UploadImgToStorageAsync>

        private static void loadDatePatterns()
        {
            // Load the patterns to use for RegEx and DateTime Parse
            DatePattern datePattern;

            datePattern = new DatePattern();
            datePattern.regex = new Regex(@"(19|20)\d{2}-((0[1-9])|(1[012]))_(19|20)\d{2}((0[1-9])|(1[012]))((0[1-9]|[12]\d)|3[01])");
            datePattern.dateParseFormat = "yyyy-MM_yyyyMMdd";
            dpList.Add(datePattern);

            datePattern = new DatePattern();
            datePattern.regex = new Regex(@"IMG_(19|20)\d{2}((0[1-9])|(1[012]))((0[1-9]|[12]\d)|3[01])");
            datePattern.dateParseFormat = "IMG_yyyyMMdd";
            dpList.Add(datePattern);

            datePattern = new DatePattern();
            datePattern.regex = new Regex(@"(19|20)\d{2}((0[1-9])|(1[012]))((0[1-9]|[12]\d)|3[01])_\d{9}_iOS");
            datePattern.dateParseFormat = "yyyyMMdd_iOS";
            dpList.Add(datePattern);

            datePattern = new DatePattern();
            datePattern.regex = new Regex(@"(19|20)\d{2}((0[1-9])|(1[012]))((0[1-9]|[12]\d)|3[01])");
            datePattern.dateParseFormat = "yyyyMMdd";
            dpList.Add(datePattern);
            // \d{4} to (19|20)\d{2}
            //+		fi	{D:\Photos\1 John J Kauflin\2016-to-2022\2018\01 Winter\FB_IMG_1520381172965.jpg}	System.IO.FileInfo

            datePattern = new DatePattern();
            datePattern.regex = new Regex(@"(19|20)\d{2}-((0[1-9])|(1[012]))-((0[1-9]|[12]\d)|3[01])");
            datePattern.dateParseFormat = "yyyy-MM-dd";
            dpList.Add(datePattern);

            datePattern = new DatePattern();
            datePattern.regex = new Regex(@"(19|20)\d{2}_((0[1-9])|(1[012]))_((0[1-9]|[12]\d)|3[01])");
            datePattern.dateParseFormat = "yyyy_MM_dd";
            dpList.Add(datePattern);

            datePattern = new DatePattern();
            datePattern.regex = new Regex(@"(19|20)\d{2}-((0[1-9])|(1[012]))");
            datePattern.dateParseFormat = "yyyy-MM";
            dpList.Add(datePattern);

            datePattern = new DatePattern();
            datePattern.regex = new Regex(@"(19|20)\d{2}_((0[1-9])|(1[012]))");
            datePattern.dateParseFormat = "yyyy_MM";
            dpList.Add(datePattern);

            datePattern = new DatePattern();
            datePattern.regex = new Regex(@"(19|20)\d{2}((0[1-9])|(1[012]))");
            datePattern.dateParseFormat = "yyyyMM";
            dpList.Add(datePattern);

            datePattern = new DatePattern();
            datePattern.regex = new Regex(@"\\(19|20)\d{2}(\-|\ )");
            datePattern.dateParseFormat = "yyyy";
            dpList.Add(datePattern);

            datePattern = new DatePattern();
            datePattern.regex = new Regex(@"(\(|\\)(19|20)\d{2}(\)|\\)");
            datePattern.dateParseFormat = "yyyy";
            dpList.Add(datePattern);

            datePattern = new DatePattern();
            datePattern.regex = new Regex(@"(19|20)\d{2} ");
            datePattern.dateParseFormat = "yyyy ";
            dpList.Add(datePattern);

            datePattern = new DatePattern();
            datePattern.regex = new Regex(@" (19|20)\d{2}");
            datePattern.dateParseFormat = " yyyy";
            dpList.Add(datePattern);
        }

        private DateTime getDateFromFilename(string fileName)
        {
            DateTime outDateTime = new DateTime(9999, 1, 1);
            string dateFormat;
            string dateStr;

            if (fileName.Contains("FB_IMG_"))
            {
                return outDateTime;
            }

            MatchCollection matches;
            bool found = false;
            int index = 0;
            // Loop through the defined RegEx patterns for date, find matches in the filename, and parse to get DateTime
            while (index < dpList.Count && !found)
            {
                matches = dpList[index].regex.Matches(fileName);
                if (matches.Count > 0)
                {
                    found = true;
                    // If there are multiple matches, just take the last one
                    dateStr = matches[matches.Count - 1].Value;
                    dateFormat = dpList[index].dateParseFormat;

                    // For this combined case, get the year-month from the start
                    if (dateFormat.Equals("yyyy-MM_yyyyMMdd"))
                    {
                        dateStr = dateStr.Substring(0, 7);
                        dateFormat = "yyyy-MM";
                    }

                    // Majority case - backup from iPhone iOS photos
                    if (dateFormat.Equals("yyyyMMdd_iOS"))
                    {
                        /*
                        20241017_090331090_iOS
                        yyyyMMdd_HHmmssfff_iOS
                        */
                        // 2024-10-28 JJK - Add minutes and seconds to the iOS parse (based on how the file name is created on download)
                        //dateStr = dateStr.Substring(0, 8);
                        dateStr = dateStr.Substring(0, 15);
                        //dateFormat = "yyyyMMdd";
                        dateFormat = "yyyyMMdd_HHmmss";
                    }

                    if (dateFormat.Equals("IMG_yyyyMMdd"))
                    {
                        dateStr = dateStr.Substring(4, 8);
                        dateFormat = "yyyyMMdd";
                    }

                    if (dateFormat.Equals("yyyy"))
                    {
                        // Strip off the beginning and ending characters ("\" or "(") form the year match
                        dateStr = dateStr.Substring(1, 4);

                        // Check for a season tag and add a month to the year
                        if (fileName.Contains(" Winter"))
                        {
                            dateFormat = "yyyy-MM";
                            if (fileName.Contains("01 Winter"))
                            {
                                dateStr = dateStr + "-01";
                            }
                            else
                            {
                                dateStr = dateStr + "-11";
                            }
                        }
                        else if (fileName.Contains(" Spring"))
                        {
                            dateFormat = "yyyy-MM";
                            dateStr = dateStr + "-04";
                        }
                        else if (fileName.Contains(" Summer"))
                        {
                            dateFormat = "yyyy-MM";
                            dateStr = dateStr + "-07";
                        }
                        else if (fileName.Contains(" Fall"))
                        {
                            dateFormat = "yyyy-MM";
                            dateStr = dateStr + "-09";
                        }
                    }

                    if (dateFormat.Equals("yyyy "))
                    {
                        // Strip off the beginning and ending characters ("\" or "(") form the year match
                        dateStr = dateStr.Substring(0, 4);
                        dateFormat = "yyyy";
                    }
                    if (dateFormat.Equals(" yyyy"))
                    {
                        // Strip off the beginning and ending characters ("\" or "(") form the year match
                        dateStr = dateStr.Substring(1, 4);
                        dateFormat = "yyyy";
                    }

                    //if (DateTime.TryParseExact(dateStr, dateFormat, null, System.Globalization.DateTimeStyles.None, out outDateTime))
                    // Modified to assume that the datetime in the filename format (from iPhone iOS) is a UTC datetime - this will make sure the datetime gets
                    // converted to local datetime for an accurate datetime of when the photo was taken
                    if (DateTime.TryParseExact(dateStr, dateFormat, null, System.Globalization.DateTimeStyles.AssumeUniversal, out outDateTime))
                    {
                        //log($"{fileName}, date: {dateStr}, format: {dateFormat}, DateTime: {outDateTime}");
                    }
                    else
                    {
                        Console.WriteLine($"{fileName}, date: {dateStr}, format: {dateFormat}, *** PARSE FAILED ***");
                    }
                }

                index++;
            }

            return outDateTime;
        }

        private DateTime setPhotoMetadata(FileInfo fi)
        {
            DateTime taken = DateTime.Parse("01/01/1800");

            //-----------------------------------------------------------------------------------------------------------------
            // Get the metadata from the photo files
            //-----------------------------------------------------------------------------------------------------------------
            try
            {
                var file = ImageFile.FromFile(fi.FullName);
                var exifArtist = file.Properties.Get<ExifAscii>(ExifTag.Artist);
                var exifCopyright = file.Properties.Get<ExifAscii>(ExifTag.Copyright);

                // Always the best option to get a nice taken datetime from the picture metadata
                var exifDateTimeOriginal = file.Properties.Get<ExifDateTime>(ExifTag.DateTimeOriginal);

                // Try to get the Date+Time taken from the filename (this will be the majority case from the iPhone iOS backup)
                taken = getDateFromFilename(fi.FullName);

                // Good philosophy to use the internal exif datetime if there - that gives the best taken datetime (as opposed to the file datetime)

                if (exifDateTimeOriginal == null)
                {
                    file.Properties.Set(ExifTag.DateTimeOriginal, taken);
                }
                else
                {
                    // If the Date from the filename is less than the Original DateTime, and it's more than 24 hours different,
                    // then set the Original to the earlier value
                    // 2024-10-28 JJK - Just make it a compare with earlist value (exif or file)
                    // if (exifDateTimeOriginal.Value.CompareTo(taken) > 0 && exifDateTimeOriginal.Value.Subtract(taken).TotalHours.CompareTo(24) > 0)
                    if (exifDateTimeOriginal.Value.CompareTo(taken) > 0)
                    {
                            file.Properties.Set(ExifTag.DateTimeOriginal, taken);
                    }
                    else
                    {
                        // If the exif is a good value, set the taken to the earlier date (from photo metadata)
                        if (exifDateTimeOriginal.Value.CompareTo(minDateTime) > 0)
                        {
                            taken = exifDateTimeOriginal.Value;
                        }
                    }
                }

                // If greater than create date, just use create date
                if (taken.CompareTo(fi.CreationTime) > 0)
                {
                    taken = fi.CreationTime;
                }

                file.Properties.Set(ExifTag.Artist, author);                                // John J Kauflin
                file.Properties.Set(ExifTag.Copyright, taken.ToString("yyyy ") + author);   // YYYY John J Kauflin
                file.Save(fi.FullName);

            }
            catch (Exception ex)
            {
                Console.WriteLine($" *** Error getting file metadata: {ex}");

                //-----------------------------------------------------------------------------------------------------------------
                // Get the photo date taken from the file name
                //-----------------------------------------------------------------------------------------------------------------
                taken = getDateFromFilename(fi.FullName);
                // If greater than create date, just use create date
                if (taken.CompareTo(fi.CreationTime) > 0)
                {
                    taken = fi.CreationTime;
                }
            }

            if (taken.ToString("yyyy").Equals("0000"))
            {
                taken = DateTime.Parse("01/01/1800");
            }

            return taken;
        }


    } //   class Program
} // namespace MediaGalleryConsole


