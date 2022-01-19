using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace WinEarth
{
    class Program
    {
        private static string storagePath = @"C:\Users\larry\Downloads\Desktop\WinEarth";

        static void Main(string[] args)
        {
            Wallpaper[] screens = { new Wallpaper(2), new Wallpaper(0), new Wallpaper(1) }; // order comes from the monitor order in Displays
            WebClient[] clients = { new WebClient(), new WebClient(), new WebClient() };
            Uri[] page_urls =
            {
                    new Uri("https://www.star.nesdis.noaa.gov/GOES/conus.php?sat=G16"),
                    new Uri("https://www.star.nesdis.noaa.gov/GOES/sector.php?sat=G16&sector=eus"),
                    new Uri("https://www.star.nesdis.noaa.gov/GOES/sector.php?sat=G16&sector=ne")
            };
            string[] filenames = { "left.png", "center.png", "right.png" };
            List<Task> tasks = new List<Task>();
            while (true)
            {
                for (int i = 0; i < 3; i++)
                {
                    tasks.Add(DownloadImageFileAsync(page_urls[i], Path.Combine(storagePath, filenames[i]), screens[i], clients[i]));
                }
                Task.WhenAll(tasks).Wait();
                Thread.Sleep(300000);
            }
        }
        private static async Task DownloadImageFileAsync(Uri fullUrl, string filePath, Wallpaper screen, WebClient client)
        {
            string imageUrl = GetImageUrl(await client.DownloadStringTaskAsync(fullUrl));
            await client.DownloadFileTaskAsync(imageUrl, filePath);
            screen.Set(filePath);
        }
        private static string GetImageUrl(string html)
        {
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            var table = htmlDoc.DocumentNode.Descendants("div").Where(node => node.GetAttributeValue("class", "").Contains("Links")).ToList();
            return table[0].Descendants("a").ToArray<HtmlNode>()[4].Attributes["href"].Value;
        }
    }
}
