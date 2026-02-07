using HyPrism.Models;

namespace HyPrism.Services.Core;

public interface INewsService
{
    Task<List<NewsItemResponse>> GetNewsAsync(int count = 10, NewsSource source = NewsSource.All);
}
