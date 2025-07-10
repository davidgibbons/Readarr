using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore.Model;
using NzbDrone.Core.MetadataSource.MachineLearning;

namespace NzbDrone.Core.MetadataSource.Community
{
    public interface ICommunityMetadataService
    {
        Task<MetadataContribution> SubmitContributionAsync(MetadataContribution contribution);
        Task<List<MetadataContribution>> GetContributionsAsync(string resourceId, string resourceType);
        Task<List<MetadataContribution>> GetPendingContributionsAsync();
        Task<List<MetadataContribution>> GetUserContributionsAsync(string userId);
        Task<MetadataContribution> GetContributionAsync(int contributionId);
        Task VoteOnContributionAsync(int contributionId, string userId, VoteType voteType);
        Task<MetadataContribution> ApproveContributionAsync(int contributionId, string moderatorId);
        Task<MetadataContribution> RejectContributionAsync(int contributionId, string moderatorId, string reason);
        Task<List<MetadataContribution>> GetModerationQueueAsync();
        Task<ContributionAnalytics> GetContributionAnalyticsAsync();
        Task<UserReputation> GetUserReputationAsync(string userId);
        Task UpdateUserReputationAsync(string userId, int points);
    }

    public class CommunityMetadataService : ICommunityMetadataService
    {
        private readonly IEnhancedMetadataService _metadataService;
        private readonly IMetadataMatchingService _matchingService;
        private readonly IMetadataBookRepository _metadataBookRepository;
        private readonly IMetadataAuthorRepository _metadataAuthorRepository;
        private readonly IMetadataContributionRepository _contributionRepository;
        private readonly IContributionVoteRepository _voteRepository;
        private readonly IUserReputationRepository _reputationRepository;
        private readonly IModerationService _moderationService;
        private readonly Logger _logger;

        public CommunityMetadataService(
            IEnhancedMetadataService metadataService,
            IMetadataMatchingService matchingService,
            IMetadataBookRepository metadataBookRepository,
            IMetadataAuthorRepository metadataAuthorRepository,
            IMetadataContributionRepository contributionRepository,
            IContributionVoteRepository voteRepository,
            IUserReputationRepository reputationRepository,
            IModerationService moderationService,
            Logger logger)
        {
            _metadataService = metadataService;
            _matchingService = matchingService;
            _metadataBookRepository = metadataBookRepository;
            _metadataAuthorRepository = metadataAuthorRepository;
            _contributionRepository = contributionRepository;
            _voteRepository = voteRepository;
            _reputationRepository = reputationRepository;
            _moderationService = moderationService;
            _logger = logger;
        }

        public async Task<MetadataContribution> SubmitContributionAsync(MetadataContribution contribution)
        {
            try
            {
                _logger.Debug($"User {contribution.UserId} submitting contribution for {contribution.ResourceType} {contribution.ResourceId}");

                // Validate contribution
                await ValidateContributionAsync(contribution);

                // Check for duplicate contributions
                var existingContributions = await _contributionRepository.GetByResourceAsync(contribution.ResourceId, contribution.ResourceType);
                var duplicate = existingContributions.FirstOrDefault(c => 
                    c.UserId == contribution.UserId && 
                    c.ContributionType == contribution.ContributionType &&
                    c.Status == ContributionStatus.Pending);

                if (duplicate != null)
                {
                    throw new InvalidOperationException("Duplicate contribution already exists");
                }

                // Set initial status and timestamps
                contribution.Status = ContributionStatus.Pending;
                contribution.CreatedAt = DateTime.UtcNow;
                contribution.UpdatedAt = DateTime.UtcNow;
                contribution.VoteCount = 0;
                contribution.Score = 0;

                // Auto-approve if user has high reputation
                var userReputation = await GetUserReputationAsync(contribution.UserId);
                if (userReputation.ReputationLevel >= ReputationLevel.Trusted)
                {
                    contribution.Status = ContributionStatus.Approved;
                    contribution.ApprovedAt = DateTime.UtcNow;
                    contribution.ApprovedBy = "auto-approval";
                    
                    // Apply contribution immediately
                    await ApplyContributionAsync(contribution);
                }

                // Save contribution
                contribution = await _contributionRepository.InsertAsync(contribution);

                // Award reputation points for contribution
                await UpdateUserReputationAsync(contribution.UserId, 5);

                _logger.Info($"Contribution {contribution.Id} submitted by user {contribution.UserId}");
                return contribution;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to submit contribution by user {contribution.UserId}");
                throw;
            }
        }

        public async Task<List<MetadataContribution>> GetContributionsAsync(string resourceId, string resourceType)
        {
            try
            {
                return await _contributionRepository.GetByResourceAsync(resourceId, resourceType);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get contributions for {resourceType} {resourceId}");
                return new List<MetadataContribution>();
            }
        }

        public async Task<List<MetadataContribution>> GetPendingContributionsAsync()
        {
            try
            {
                return await _contributionRepository.GetPendingAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get pending contributions");
                return new List<MetadataContribution>();
            }
        }

        public async Task<List<MetadataContribution>> GetUserContributionsAsync(string userId)
        {
            try
            {
                return await _contributionRepository.GetByUserAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get contributions for user {userId}");
                return new List<MetadataContribution>();
            }
        }

        public async Task<MetadataContribution> GetContributionAsync(int contributionId)
        {
            try
            {
                return await _contributionRepository.GetByIdAsync(contributionId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get contribution {contributionId}");
                return null;
            }
        }

        public async Task VoteOnContributionAsync(int contributionId, string userId, VoteType voteType)
        {
            try
            {
                _logger.Debug($"User {userId} voting {voteType} on contribution {contributionId}");

                var contribution = await GetContributionAsync(contributionId);
                if (contribution == null)
                {
                    throw new ArgumentException($"Contribution {contributionId} not found");
                }

                if (contribution.Status != ContributionStatus.Pending)
                {
                    throw new InvalidOperationException("Can only vote on pending contributions");
                }

                // Check if user already voted
                var existingVote = await _voteRepository.GetByUserAndContributionAsync(userId, contributionId);
                if (existingVote != null)
                {
                    // Update existing vote
                    existingVote.VoteType = voteType;
                    existingVote.UpdatedAt = DateTime.UtcNow;
                    await _voteRepository.UpdateAsync(existingVote);
                }
                else
                {
                    // Create new vote
                    var vote = new ContributionVote
                    {
                        ContributionId = contributionId,
                        UserId = userId,
                        VoteType = voteType,
                        CreatedAt = DateTime.UtcNow
                    };
                    await _voteRepository.InsertAsync(vote);
                }

                // Update contribution score
                await UpdateContributionScoreAsync(contributionId);

                // Check if contribution should be auto-approved/rejected
                await CheckContributionThresholdAsync(contributionId);

                _logger.Debug($"Vote recorded: {voteType} on contribution {contributionId} by user {userId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to vote on contribution {contributionId} by user {userId}");
                throw;
            }
        }

        public async Task<MetadataContribution> ApproveContributionAsync(int contributionId, string moderatorId)
        {
            try
            {
                _logger.Info($"Moderator {moderatorId} approving contribution {contributionId}");

                var contribution = await GetContributionAsync(contributionId);
                if (contribution == null)
                {
                    throw new ArgumentException($"Contribution {contributionId} not found");
                }

                if (contribution.Status != ContributionStatus.Pending)
                {
                    throw new InvalidOperationException("Can only approve pending contributions");
                }

                // Apply the contribution
                await ApplyContributionAsync(contribution);

                // Update contribution status
                contribution.Status = ContributionStatus.Approved;
                contribution.ApprovedAt = DateTime.UtcNow;
                contribution.ApprovedBy = moderatorId;
                contribution.UpdatedAt = DateTime.UtcNow;

                contribution = await _contributionRepository.UpdateAsync(contribution);

                // Award reputation points to contributor
                await UpdateUserReputationAsync(contribution.UserId, 10);

                _logger.Info($"Contribution {contributionId} approved by moderator {moderatorId}");
                return contribution;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to approve contribution {contributionId} by moderator {moderatorId}");
                throw;
            }
        }

        public async Task<MetadataContribution> RejectContributionAsync(int contributionId, string moderatorId, string reason)
        {
            try
            {
                _logger.Info($"Moderator {moderatorId} rejecting contribution {contributionId}: {reason}");

                var contribution = await GetContributionAsync(contributionId);
                if (contribution == null)
                {
                    throw new ArgumentException($"Contribution {contributionId} not found");
                }

                if (contribution.Status != ContributionStatus.Pending)
                {
                    throw new InvalidOperationException("Can only reject pending contributions");
                }

                // Update contribution status
                contribution.Status = ContributionStatus.Rejected;
                contribution.RejectedAt = DateTime.UtcNow;
                contribution.RejectedBy = moderatorId;
                contribution.RejectionReason = reason;
                contribution.UpdatedAt = DateTime.UtcNow;

                contribution = await _contributionRepository.UpdateAsync(contribution);

                // Deduct reputation points from contributor
                await UpdateUserReputationAsync(contribution.UserId, -5);

                _logger.Info($"Contribution {contributionId} rejected by moderator {moderatorId}");
                return contribution;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to reject contribution {contributionId} by moderator {moderatorId}");
                throw;
            }
        }

        public async Task<List<MetadataContribution>> GetModerationQueueAsync()
        {
            try
            {
                return await _contributionRepository.GetForModerationAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get moderation queue");
                return new List<MetadataContribution>();
            }
        }

        public async Task<ContributionAnalytics> GetContributionAnalyticsAsync()
        {
            try
            {
                var analytics = new ContributionAnalytics
                {
                    TotalContributions = await _contributionRepository.GetCountAsync(),
                    PendingContributions = await _contributionRepository.GetPendingCountAsync(),
                    ApprovedContributions = await _contributionRepository.GetApprovedCountAsync(),
                    RejectedContributions = await _contributionRepository.GetRejectedCountAsync(),
                    AverageProcessingTime = await _contributionRepository.GetAverageProcessingTimeAsync(),
                    TopContributors = await GetTopContributorsAsync(),
                    ContributionTrends = await GetContributionTrendsAsync()
                };

                return analytics;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get contribution analytics");
                return new ContributionAnalytics();
            }
        }

        public async Task<UserReputation> GetUserReputationAsync(string userId)
        {
            try
            {
                var reputation = await _reputationRepository.GetByUserIdAsync(userId);
                if (reputation == null)
                {
                    // Create new reputation record
                    reputation = new UserReputation
                    {
                        UserId = userId,
                        Points = 0,
                        ReputationLevel = ReputationLevel.New,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    reputation = await _reputationRepository.InsertAsync(reputation);
                }

                return reputation;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get reputation for user {userId}");
                return new UserReputation { UserId = userId, Points = 0, ReputationLevel = ReputationLevel.New };
            }
        }

        public async Task UpdateUserReputationAsync(string userId, int points)
        {
            try
            {
                var reputation = await GetUserReputationAsync(userId);
                reputation.Points += points;
                reputation.UpdatedAt = DateTime.UtcNow;

                // Update reputation level based on points
                reputation.ReputationLevel = CalculateReputationLevel(reputation.Points);

                await _reputationRepository.UpdateAsync(reputation);

                _logger.Debug($"Updated reputation for user {userId}: {points} points (total: {reputation.Points})");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to update reputation for user {userId}");
            }
        }

        #region Private Methods

        private async Task ValidateContributionAsync(MetadataContribution contribution)
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(contribution.UserId))
                throw new ArgumentException("UserId is required");

            if (string.IsNullOrWhiteSpace(contribution.ResourceId))
                throw new ArgumentException("ResourceId is required");

            if (string.IsNullOrWhiteSpace(contribution.ResourceType))
                throw new ArgumentException("ResourceType is required");

            if (string.IsNullOrWhiteSpace(contribution.ContributionType))
                throw new ArgumentException("ContributionType is required");

            // Validate resource exists
            switch (contribution.ResourceType.ToLowerInvariant())
            {
                case "book":
                    var book = await _metadataBookRepository.GetByIdentifierAsync(contribution.ResourceId);
                    if (book == null)
                        throw new ArgumentException($"Book {contribution.ResourceId} not found");
                    break;
                case "author":
                    var author = await _metadataAuthorRepository.GetByIdentifierAsync(contribution.ResourceId);
                    if (author == null)
                        throw new ArgumentException($"Author {contribution.ResourceId} not found");
                    break;
                default:
                    throw new ArgumentException($"Unknown resource type: {contribution.ResourceType}");
            }

            // Content moderation
            await _moderationService.ValidateContentAsync(contribution);
        }

        private async Task ApplyContributionAsync(MetadataContribution contribution)
        {
            try
            {
                switch (contribution.ResourceType.ToLowerInvariant())
                {
                    case "book":
                        await ApplyBookContributionAsync(contribution);
                        break;
                    case "author":
                        await ApplyAuthorContributionAsync(contribution);
                        break;
                    default:
                        throw new ArgumentException($"Unknown resource type: {contribution.ResourceType}");
                }

                _logger.Debug($"Applied contribution {contribution.Id} to {contribution.ResourceType} {contribution.ResourceId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to apply contribution {contribution.Id}");
                throw;
            }
        }

        private async Task ApplyBookContributionAsync(MetadataContribution contribution)
        {
            var metadataBook = await _metadataBookRepository.GetByIdentifierAsync(contribution.ResourceId);
            if (metadataBook == null) return;

            switch (contribution.ContributionType.ToLowerInvariant())
            {
                case "title":
                    metadataBook.Title = contribution.NewValue;
                    break;
                case "description":
                    metadataBook.Description = contribution.NewValue;
                    break;
                case "publication_date":
                    if (DateTime.TryParse(contribution.NewValue, out var pubDate))
                        metadataBook.PublicationDate = pubDate;
                    break;
                case "page_count":
                    if (int.TryParse(contribution.NewValue, out var pageCount))
                        metadataBook.PageCount = pageCount;
                    break;
                case "language":
                    metadataBook.Language = contribution.NewValue;
                    break;
                default:
                    _logger.Warn($"Unknown contribution type: {contribution.ContributionType}");
                    return;
            }

            metadataBook.UpdatedAt = DateTime.UtcNow;
            metadataBook.UpdateConfidenceScore();
            await _metadataBookRepository.UpdateAsync(metadataBook);
        }

        private async Task ApplyAuthorContributionAsync(MetadataContribution contribution)
        {
            var metadataAuthor = await _metadataAuthorRepository.GetByIdentifierAsync(contribution.ResourceId);
            if (metadataAuthor == null) return;

            switch (contribution.ContributionType.ToLowerInvariant())
            {
                case "name":
                    metadataAuthor.Name = contribution.NewValue;
                    break;
                case "biography":
                    metadataAuthor.Biography = contribution.NewValue;
                    break;
                case "birth_date":
                    if (DateTime.TryParse(contribution.NewValue, out var birthDate))
                        metadataAuthor.BirthDate = birthDate;
                    break;
                case "death_date":
                    if (DateTime.TryParse(contribution.NewValue, out var deathDate))
                        metadataAuthor.DeathDate = deathDate;
                    break;
                default:
                    _logger.Warn($"Unknown contribution type: {contribution.ContributionType}");
                    return;
            }

            metadataAuthor.UpdatedAt = DateTime.UtcNow;
            metadataAuthor.UpdateConfidenceScore();
            await _metadataAuthorRepository.UpdateAsync(metadataAuthor);
        }

        private async Task UpdateContributionScoreAsync(int contributionId)
        {
            var votes = await _voteRepository.GetByContributionAsync(contributionId);
            var score = votes.Sum(v => v.VoteType == VoteType.Up ? 1 : -1);

            var contribution = await GetContributionAsync(contributionId);
            if (contribution != null)
            {
                contribution.Score = score;
                contribution.VoteCount = votes.Count;
                contribution.UpdatedAt = DateTime.UtcNow;
                await _contributionRepository.UpdateAsync(contribution);
            }
        }

        private async Task CheckContributionThresholdAsync(int contributionId)
        {
            var contribution = await GetContributionAsync(contributionId);
            if (contribution == null) return;

            // Auto-approve if score >= 5 and at least 3 votes
            if (contribution.Score >= 5 && contribution.VoteCount >= 3)
            {
                await ApproveContributionAsync(contributionId, "auto-approval");
            }
            // Auto-reject if score <= -3 and at least 2 votes
            else if (contribution.Score <= -3 && contribution.VoteCount >= 2)
            {
                await RejectContributionAsync(contributionId, "auto-rejection", "Community consensus");
            }
        }

        private ReputationLevel CalculateReputationLevel(int points)
        {
            return points switch
            {
                >= 1000 => ReputationLevel.Expert,
                >= 500 => ReputationLevel.Trusted,
                >= 100 => ReputationLevel.Regular,
                >= 10 => ReputationLevel.Contributor,
                _ => ReputationLevel.New
            };
        }

        private async Task<List<TopContributor>> GetTopContributorsAsync()
        {
            // This would typically query analytics data
            // For now, return sample data
            return new List<TopContributor>
            {
                new TopContributor { UserId = "user1", Contributions = 50, Reputation = 750 },
                new TopContributor { UserId = "user2", Contributions = 35, Reputation = 500 },
                new TopContributor { UserId = "user3", Contributions = 25, Reputation = 300 }
            };
        }

        private async Task<List<ContributionTrend>> GetContributionTrendsAsync()
        {
            // This would typically query analytics data
            // For now, return sample data
            return new List<ContributionTrend>
            {
                new ContributionTrend { Date = DateTime.UtcNow.AddDays(-7), Count = 25, Type = "book" },
                new ContributionTrend { Date = DateTime.UtcNow.AddDays(-6), Count = 30, Type = "author" },
                new ContributionTrend { Date = DateTime.UtcNow.AddDays(-5), Count = 20, Type = "book" }
            };
        }

        #endregion
    }

    #region Models

    public class MetadataContribution : ModelBase
    {
        public string UserId { get; set; }
        public string ResourceId { get; set; }
        public string ResourceType { get; set; } // "book" or "author"
        public string ContributionType { get; set; } // "title", "description", "biography", etc.
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Reason { get; set; }
        public ContributionStatus Status { get; set; }
        public int VoteCount { get; set; }
        public int Score { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string ApprovedBy { get; set; }
        public DateTime? RejectedAt { get; set; }
        public string RejectedBy { get; set; }
        public string RejectionReason { get; set; }
    }

    public enum ContributionStatus
    {
        Pending,
        Approved,
        Rejected
    }

    public class ContributionVote : ModelBase
    {
        public int ContributionId { get; set; }
        public string UserId { get; set; }
        public VoteType VoteType { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public enum VoteType
    {
        Up,
        Down
    }

    public class UserReputation : ModelBase
    {
        public string UserId { get; set; }
        public int Points { get; set; }
        public ReputationLevel ReputationLevel { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public enum ReputationLevel
    {
        New,
        Contributor,
        Regular,
        Trusted,
        Expert
    }

    public class ContributionAnalytics
    {
        public int TotalContributions { get; set; }
        public int PendingContributions { get; set; }
        public int ApprovedContributions { get; set; }
        public int RejectedContributions { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
        public List<TopContributor> TopContributors { get; set; } = new List<TopContributor>();
        public List<ContributionTrend> ContributionTrends { get; set; } = new List<ContributionTrend>();
    }

    public class TopContributor
    {
        public string UserId { get; set; }
        public int Contributions { get; set; }
        public int Reputation { get; set; }
    }

    public class ContributionTrend
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
        public string Type { get; set; }
    }

    #endregion

    #region Repository Interfaces (to be implemented)

    public interface IMetadataContributionRepository : IBasicRepository<MetadataContribution>
    {
        Task<List<MetadataContribution>> GetByResourceAsync(string resourceId, string resourceType);
        Task<List<MetadataContribution>> GetPendingAsync();
        Task<List<MetadataContribution>> GetByUserAsync(string userId);
        Task<List<MetadataContribution>> GetForModerationAsync();
        Task<int> GetPendingCountAsync();
        Task<int> GetApprovedCountAsync();
        Task<int> GetRejectedCountAsync();
        Task<TimeSpan> GetAverageProcessingTimeAsync();
    }

    public interface IContributionVoteRepository : IBasicRepository<ContributionVote>
    {
        Task<ContributionVote> GetByUserAndContributionAsync(string userId, int contributionId);
        Task<List<ContributionVote>> GetByContributionAsync(int contributionId);
    }

    public interface IUserReputationRepository : IBasicRepository<UserReputation>
    {
        Task<UserReputation> GetByUserIdAsync(string userId);
        Task<List<UserReputation>> GetTopReputationsAsync(int limit);
    }

    public interface IModerationService
    {
        Task ValidateContentAsync(MetadataContribution contribution);
        Task<bool> IsSpamAsync(string content);
        Task<bool> IsInappropriateAsync(string content);
    }

    #endregion
} 