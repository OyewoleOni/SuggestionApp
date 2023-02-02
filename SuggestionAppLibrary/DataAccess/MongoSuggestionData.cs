
namespace SuggestionAppLibrary.DataAccess;

public class MongoSuggestionData : ISuggestionData
{
   private readonly IMongoCollection<SuggestionModel> _suggestions;
   private readonly IDbConnection _db;
   private readonly IUserData _userData;
   private readonly IMemoryCache _cache;
   private const string CACHENAME = "SuggestionData";

   public MongoSuggestionData(IDbConnection db, IUserData userData, IMemoryCache cache)
   {
      _suggestions = db.SuggestionCollection;
      _db = db;
      _userData = userData;
      _cache = cache;
   }

   public async Task<List<SuggestionModel>> GetAllSuggestions()
   {
      var output = _cache.Get<List<SuggestionModel>>(CACHENAME);
      if (output is null)
      {
         var results = await _suggestions.FindAsync(s => s.Archived == false);
         output = results.ToList();

         _cache.Set(CACHENAME, output, TimeSpan.FromMinutes(1));
      }
      return output;
   }

   public async Task<List<SuggestionModel>> GetAllApprovedSuggestions()
   {
      var output = await GetAllSuggestions();
      return output.Where(s => s.ApproveForRelease).ToList();
   }

   public async Task<SuggestionModel> GetSuggestion(string id)
   {
      var result = await _suggestions.FindAsync(s => s.Id == id);
      return result.FirstOrDefault();
   }

   public async Task<List<SuggestionModel>> GetAllSuggestionsWaitingForApproval()
   {
      var output = await GetAllSuggestions();
      return output.Where(s =>
      s.ApproveForRelease == false
      && s.Rejected == false).ToList();
   }

   public async Task UpdateSuggestion(SuggestionModel suggestion)
   {
      await _suggestions.ReplaceOneAsync(s => s.Id == suggestion.Id, suggestion);
      _cache.Remove(CACHENAME);
   }

   public async Task UpvoteSuggestion(string suggestionId, string userId)
   {
      var client = _db.Client;
      using var session = await client.StartSessionAsync();
      session.StartTransaction();

      try
      {
         var db = client.GetDatabase(_db.DbName);
         var suggestionInTransaction = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
         var suggestion = (await suggestionInTransaction.FindAsync(s => s.Id == suggestionId)).First();

         bool isUpvote = suggestion.UserVotes.Add(userId);
         if (isUpvote == false)
         {
            suggestion.UserVotes.Remove(userId);
         }
         await suggestionInTransaction.ReplaceOneAsync(s => s.Id == suggestionId, suggestion);

         var usersInTransaction = db.GetCollection<UserModel>(_db.UserCollectionName);
         var user = await _userData.GetUserAsync(suggestion.Author.Id);
         if (isUpvote)
         {
            user.VotedOnSuggestions.Add(new BasicSuggestionModel(suggestion));
         }
         else
         {
            var suggestionToRemove = user.VotedOnSuggestions.Where(s => s.Id == suggestionId).First();
            user.VotedOnSuggestions.Remove(suggestionToRemove);
         }
         await usersInTransaction.ReplaceOneAsync(u => u.Id == userId, user);
         await session.CommitTransactionAsync();
         _cache.Remove(CACHENAME);
      }
      catch (Exception ex)
      {
         await session.AbortTransactionAsync();
         throw;
      }
   }

   public async Task CreateSuggestion(SuggestionModel suggestion)
   {
      var client = _db.Client;

      using var session = await client.StartSessionAsync();

      session.StartTransaction();

      try
      {
         var db = client.GetDatabase(_db.DbName);
         var suggestionInTransaction = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
         await suggestionInTransaction.InsertOneAsync(suggestion);

         var usersInTransaction = db.GetCollection<UserModel>(_db.UserCollectionName);
         var user = await _userData.GetUserAsync(suggestion.Author.Id);
         user.AuthoredSuggestions.Add(new BasicSuggestionModel(suggestion));
         await usersInTransaction.ReplaceOneAsync(u => u.Id == user.Id, user);

         await session.CommitTransactionAsync();
      }
      catch (Exception ex)
      {
         await session.AbortTransactionAsync();
         throw;
      }
   }

}
