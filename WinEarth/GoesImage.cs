using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace WinEarth
{
    /// <summary>
    /// Shared helper for scraping a NOAA GOES page and fetching the full-resolution
    /// image it links to. Used both by the background updater and the config GUI's
    /// crop preview so the two agree on how a page/item-index resolves to an image.
    /// </summary>
    public static class GoesImage
    {
        // HttpClient is thread-safe and meant to be reused for the life of the process.
        private static readonly HttpClient httpClient = new HttpClient();

        /// <summary>
        /// Extracts the href of the <paramref name="index"/>-th anchor in the page's
        /// "Links" list. Higher indices generally correspond to higher resolutions.
        /// </summary>
        public static string ExtractImageUrl(string html, int index)
        {
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            var table = htmlDoc.DocumentNode.Descendants("div")
                .Where(node => node.GetAttributeValue("class", "").Contains("Links"))
                .ToList();
            if (table.Count == 0)
            {
                throw new InvalidOperationException("No 'Links' section found on the page.");
            }

            HtmlNode[] anchors = table[0].Descendants("a").ToArray();
            if (index < 0 || index >= anchors.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    string.Format("Item index {0} is out of range; the page has {1} link(s).", index, anchors.Length));
            }

            return anchors[index].Attributes["href"].Value;
        }

        /// <summary>
        /// Scrapes <paramref name="pageUrl"/>, resolves the item-index link (relative
        /// hrefs are resolved against the page), downloads it, and returns it as a
        /// fresh <see cref="Bitmap"/>. The caller owns the returned bitmap.
        /// </summary>
        public static async Task<Bitmap> DownloadAsync(string pageUrl, int index)
        {
            string html = await httpClient.GetStringAsync(pageUrl);
            string href = ExtractImageUrl(html, index);

            // Pages may use absolute or page-relative hrefs; Uri resolution handles both.
            Uri imageUri = new Uri(new Uri(pageUrl), href);

            using (HttpResponseMessage response = await httpClient.GetAsync(imageUri))
            {
                response.EnsureSuccessStatusCode();
                using (Stream stream = await response.Content.ReadAsStreamAsync())
                using (Image source = Image.FromStream(stream))
                {
                    // Copy off the response stream so the bitmap owns its own pixels.
                    return new Bitmap(source);
                }
            }
        }
    }
}
