
namespace SuggestionAppLibrary.DataAccess;

public class MongoCategoryData : ICategoryData
{
	private readonly IMemoryCache _cache;
	private readonly IMongoCollection<CategoryModel> _categories;
	private const string CACHENAME = "CategoryData";

	public MongoCategoryData(IDbConnection db, IMemoryCache cache)
	{
		_cache = cache;
		_categories = db.CategoryCollection;
	}

	public async Task<List<CategoryModel>> GetAllCategories()
	{
		var output = _cache.Get<List<CategoryModel>>(CACHENAME);
		if (output == null)
		{
			var results = await _categories.FindAsync(_ => true);
			output = results.ToList();

			_cache.Set(CACHENAME, output, TimeSpan.FromDays(1));
		}
		return output;
	}
	public Task CreateCategory(CategoryModel category)
	{
		return _categories.InsertOneAsync(category);
	}
}
