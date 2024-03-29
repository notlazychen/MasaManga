﻿using System;
using System.Collections.Concurrent;
using System.IO;
using MasaManga.BookSource;
using MasaManga.Data;
using MasaManga.Utils;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using static System.Reflection.Metadata.BlobBuilder;

namespace MasaManga.Services
{
    public class BookStoreService
    {
        private readonly IServiceProvider _serviceProvider;

        public BookStoreService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public List<Book> GetBooks()
        {
            using var scope = _serviceProvider.CreateScope();
            using var dbContext = scope.ServiceProvider.GetService<BookStoreDbContext>();
            var books = dbContext.Books.AsNoTracking().ToList();
            return books;
        }

        public async Task<(bool ok, string err)> AddBook(string indexUrl)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                using var dbContext = scope.ServiceProvider.GetService<BookStoreDbContext>();
                var source = SourceStore.SourceSites.FirstOrDefault(x => indexUrl.StartsWith(x.Url));
                if (source == null)
                    return (false, "源不存在");
                if(dbContext.Books.Any(x=>x.IndexUrl == indexUrl))
                    return (false, "书已添加");
                var book = new Book() { IndexUrl = indexUrl, SourceTitle = source.Title };
                source.FulfilBook(book);
                Parallel.ForEach(book.Sections, section =>
                {
                    source.FulfilSection(section);
                });
                book.TotalPage = book.Sections.Sum(x => x.Pics.Count);
                dbContext.Books.Add(book);
                await dbContext.SaveChangesAsync();
                //book.Cover = $"wwwroot/store/{book.Title}/cover.jpg";
                //var downloader = new FileDownloader();
                //await downloader.DownloadAsync(book.CoverUrl, book.Cover);
                return (true, "");
            }
            catch(Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task BeginDownload(int bookId)
        {
            using var scope = _serviceProvider.CreateScope();
            using var dbContext = scope.ServiceProvider.GetService<BookStoreDbContext>();
            var book = dbContext.Books
                .Include(x => x.Sections)
                    .ThenInclude(x => x.Pics)
                .FirstOrDefault(x=>x.Id == bookId);
            if (book == null)
                return;
            if (book.IsDownloading)
                return;
            string bookPath = $"wwwroot/store/{book.Title}";
            book.IsDownloading = true;
            book.DownloadPage = book.Sections.Sum(x => x.Pics.Count(p => p.IsDownloaded));
            dbContext.SaveChanges();
            //开启下载
            //todo: 增加多线程下载
            var downloader = new FileDownloader();
            foreach (var section in book.Sections.OrderBy(s=>s.Index))
            {
                var dirPath = Path.Combine(bookPath, section.Title);
                Directory.CreateDirectory(dirPath);
                foreach (var pic in section.Pics)
                {
                    if (pic.IsDownloaded)
                        continue;
                    string filename = Path.Combine(dirPath, pic.FileName);
                    Console.WriteLine($"开始下载：{section.Title}");
                    await downloader.DownloadAsync(pic.Url, filename);
                    pic.IsDownloaded = true;
                    book.DownloadPage++;
                    dbContext.SaveChanges();
                }
            }
            book.DownloadPage = book.Sections.Sum(x => x.Pics.Count(p => p.IsDownloaded));
            book.IsDownloading = false;
            dbContext.SaveChanges();
        }
    }
}
