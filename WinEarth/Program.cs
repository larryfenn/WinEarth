using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using HtmlAgilityPack;

namespace WinEarth
{
    static class Program
    {
        private static string storagePath = @"C:\Users\larry\Downloads\Desktop\WinEarth";

        static void Main(string[] args)
        {
            Wallpaper[] screens = { new Wallpaper(2), new Wallpaper(1), new Wallpaper(0) }; // order comes from the monitor order in Displays
            WebClient[] clients = { new WebClient(), new WebClient(), new WebClient() };
            string[] filenames = { "left.png", "center.png", "right.png" };
            Rectangle[] crops = { new Rectangle(2600, 94, 2400, 1350), new Rectangle(0, 0, 2000, 1125), new Rectangle(0, 0, 2310, 1300) };
            List<Task> tasks = new List<Task>();
            while (true)
            {
                // string[] meso_pages = GetMesoscaleUrl(clients[0]);
                Uri[] page_urls = {
                    new Uri("https://www.star.nesdis.noaa.gov/goes/conus.php?sat=G18"),
                    new Uri("https://www.star.nesdis.noaa.gov/GOES/sector.php?sat=G16&sector=eus"),
                    // new Uri(meso_pages[0]),
                    // new Uri(meso_pages[1]),
                    new Uri("https://www.star.nesdis.noaa.gov/GOES/conus.php?sat=G16")
                };
                int[] page_item_indices = { 6, 5, 5 };
                for (int i = 0; i < 3; i++)
                {
                    tasks.Add(DownloadImageFileAsync(page_urls[i], page_item_indices[i], filenames[i], crops[i], screens[i], clients[i]));
                }
                try
                {
                    Task.WhenAll(tasks).Wait();
                }
                catch (Exception e)
                {
                    
                }
                Thread.Sleep(300000);
            }
        }
        private static async Task DownloadImageFileAsync(Uri fullUrl, int index, string filename, Rectangle crop, Wallpaper screen, WebClient client)
        {
            string filePath = Path.Combine(storagePath, filename);
            bool success = false;
            int retries = 0;
            while (!success & retries < 3)
            {
                try
                {
                    string imageUrl = GetImageUrl(await client.DownloadStringTaskAsync(fullUrl), index);
                    await client.DownloadFileTaskAsync(imageUrl, filePath);
                    Crop(filePath, crop);
                    success = true;
                    screen.Set(filePath);
                }
                catch (Exception e)
                {
                    retries++;
                    using (StreamWriter sw = File.AppendText(@"C:\Users\larry\corelog.txt"))
                    {
                        sw.WriteLine(DateTime.Now + "|" + e.GetType().Name + "|" + e.Message + "|" + e.StackTrace);
                    }
                }
            }
            if (!success)
            {
                using (StreamWriter sw = File.AppendText(@"C:\Users\larry\corelog.txt"))
                {
                    sw.WriteLine("Failed to update!");
                }
            }
        }
        private static string GetImageUrl(string html, int index)
        {
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            var table = htmlDoc.DocumentNode.Descendants("div").Where(node => node.GetAttributeValue("class", "").Contains("Links")).ToList();
            return table[0].Descendants("a").ToArray<HtmlNode>()[index].Attributes["href"].Value;
        }
        private static string[] GetMesoscaleUrl(WebClient client)
        {
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(client.DownloadString("https://www.star.nesdis.noaa.gov/GOES/meso_index.php"));
            var list = htmlDoc.DocumentNode.Descendants("div").Where(node => node.GetAttributeValue("class", "").Contains("MesoScroll")).ToList();
            string[] results = new string[2];
            results[0] = "https://www.star.nesdis.noaa.gov/GOES/" + WebUtility.HtmlDecode(list[0].Descendants("a").ToArray<HtmlNode>()[0].Attributes["href"].Value);
            results[1] = "https://www.star.nesdis.noaa.gov/GOES/" + WebUtility.HtmlDecode(list[0].Descendants("a").ToArray<HtmlNode>()[1].Attributes["href"].Value);
            return results;
        }
        public static void Crop(string filePath, Rectangle crop)
        {
            Bitmap b = new Bitmap(filePath);
            using (Bitmap nb = new Bitmap(crop.Width, crop.Height))
            {
                using (Graphics g = Graphics.FromImage(nb))
                {
                    g.DrawImage(b, -crop.X, -crop.Y);
                    b.Dispose();
                    nb.Save(filePath, ImageFormat.Png);
                }
            }
        }
    }
}
