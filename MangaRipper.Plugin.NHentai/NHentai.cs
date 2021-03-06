﻿using MangaRipper.Core;
using MangaRipper.Core.Interfaces;
using MangaRipper.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MangaRipper.Plugin.NHentai
{
    public class NHentai : IMangaService
    {
        private readonly ILogger Logger;
        private readonly IDownloader downloader;
        private readonly IXPathSelector selector;
        private readonly IRetry retry;
        private readonly string SiteUrl = "https://nhentai.net";

        public NHentai(ILogger myLogger, IDownloader downloader, IXPathSelector selector, IRetry retry)
        {
            Logger = myLogger;
            this.downloader = downloader;
            this.selector = selector;
            this.retry = retry;
        }

        public SiteInformation GetInformation()
        {
            return new SiteInformation(nameof(NHentai), SiteUrl, "English, Japanese");
        }

        public bool Of(string link)
        {
            var uri = new Uri(link);
            return uri.Host.Equals("nhentai.net");
        }

        public async Task<IEnumerable<Chapter>> FindChapters(string manga, IProgress<int> progress, CancellationToken cancellationToken)
        {
            Logger.Info($@"> FindChapters(): {manga}");
            progress.Report(0);

            // find all chapters in a manga
            IEnumerable<Chapter> chaps = await retry.DoAsync(() =>
            {
                return DownloadAndParseChapters(manga, cancellationToken);
            }, TimeSpan.FromSeconds(3));
            progress.Report(100);

            return chaps;
        }

        public async Task<IEnumerable<string>> FindImages(Chapter chapter, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var pages = (await FindPagesInChapter(chapter.Url, cancellationToken));

            // find all images in pages
            int current = 0;
            var images = new List<string>();
            foreach (var page in pages)
            {
                string image = await retry.DoAsync(() => DownloadAndParseImage(page, cancellationToken), TimeSpan.FromSeconds(3));
                images.Add(image);
                var f = (float)++current / pages.Count();
                int i = Convert.ToInt32(f * 100);
                progress.Report(i);
            }
            progress.Report(100);
            return images;
        }

        private async Task<IEnumerable<string>> FindPagesInChapter(string chapterUrl, CancellationToken cancellationToken)
        {
            var input = await downloader.DownloadStringAsync(chapterUrl, cancellationToken);

            // <div...id="thumbnail-container"><div class="thumb-container"><a...href="/g/93922/1/">
            var links = selector.SelectMany(input, "//div[@id='thumbnail-container']//div[contains(@class, 'thumb-container')]//a")
                .Select(n => SiteUrl + n.Attributes["href"]);

            return links.ToArray();
        }

        private async Task<string> DownloadAndParseImage(string page, CancellationToken cancellationToken)
        {
            var pageHtml = await downloader.DownloadStringAsync(page, cancellationToken);
            // <div id="content"><div...id="page-container"><section id="image-container"...><img>
            var image = selector
            .Select(pageHtml, "//div[@id='content']//div[@id='page-container']//section[@id='image-container']//img").Attributes["src"];
            return image;
        }

        #region Private methods

        private async Task<IEnumerable<Chapter>> DownloadAndParseChapters(string manga, CancellationToken cancellationToken)
        {
            string input = await downloader.DownloadStringAsync(manga, cancellationToken);

            // In case if you open the chapter itself
            var title = selector.Select(input, "//div[@id='content']//h1").InnerHtml;

            // If you open the page with number of chapters (artist/character/Group/Tags)
            if (title.Contains("<span"))
            {
                // Title consist of three parts. e.g. [Character] [nobuna oda] [(6)]
                // Take the first two
                var titleType = selector.Select(input, "//div[@id='content']//h1//span").InnerHtml;
                var titleName = selector.Select(input, "//div[@id='content']//h1//span[2]").InnerHtml;
                title = titleType + " - " + titleName;

                var chaps = selector.SelectMany(input, "//div[contains(@class, 'container')]//a")
                        .Select(n =>
                        {
                            string name = NameResolver(n.InnerHtml);
                            string url = SiteUrl + n.Attributes["href"];
                            return new Chapter(name, url) { Manga = title };
                        });

                return chaps;
            }

            // We have only one chapter
            var chap = new Chapter(title, manga) { Manga = title };

            return new List<Chapter>() { chap };
        }

        /// <summary>
        /// Get the name from the "<div class="caption">" tag
        /// We have to use this method as we can't use "selector" inside "selector" - "startindex" out of range exception
        /// </summary>
        /// <param name="text">Text with html tags</param>
        /// <returns></returns>
        private string NameResolver(string text)
        {
            var htmlAdapter = new HtmlAtilityPackAdapter();
            return htmlAdapter.Select(text, "//div[contains(@class, 'caption')]").InnerHtml;
        }

        #endregion
    }
}
