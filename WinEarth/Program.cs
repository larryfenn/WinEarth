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
        static void Main(string[] args)
        {
            Wallpaper[] screens = { new Wallpaper(2), new Wallpaper(0), new Wallpaper(1) }; // order comes from the monitor order in Displays
            string[] page_urls =
            {
                    "https://www.star.nesdis.noaa.gov/GOES/conus.php?sat=G16",
                    "https://www.star.nesdis.noaa.gov/GOES/sector.php?sat=G16&sector=eus",
                    "https://www.star.nesdis.noaa.gov/GOES/sector.php?sat=G16&sector=ne"
            };
            while (true)
            {
                string[] image_urls = new string[3];
                for (int i = 0; i < 3; i++)
                {
                    var response = CallUrl(page_urls[i]).Result;
                    image_urls[i] = getImageUrl(response);
                }

                string[] filenames = { "left.png", "center.png", "right.png" };
                string storagePath = @"C:\Users\larry\Downloads\Desktop\EarthView";

                using (var client = new WebClient())
                {
                    for (int i = 0; i < 3; i++)
                    {
                        string storageFilename = filenames[i];
                        string picFilename = Path.Combine(storagePath, storageFilename);
                        client.DownloadFile(image_urls[i], picFilename); // TODO: handle HTTP errors
                        screens[i].Set(picFilename);
                    }
                }
                Thread.Sleep(300000);
            }
        }

        private static async Task<string> CallUrl(string fullUrl)
        {
            HttpClient client = new HttpClient();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            client.DefaultRequestHeaders.Accept.Clear();
            var response = client.GetStringAsync(fullUrl);
            return await response;

        }

        private static string getImageUrl(string html)
        {
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            var table = htmlDoc.DocumentNode.Descendants("div").Where(node => node.GetAttributeValue("class", "").Contains("Links")).ToList();
            string url = table[0].Descendants("a").ToArray<HtmlNode>()[4].Attributes["href"].Value;
            return url;
        }
    }
}
