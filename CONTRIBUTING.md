# 🤝 Contributing to Readarr Revival

## 🎉 Welcome to the Readarr Revival Community!

Thank you for your interest in contributing to the Readarr Revival Project! This guide will help you get started with contributing to our open-source book management system.

---

## 📋 Table of Contents

- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Code Standards](#code-standards)
- [Testing Guidelines](#testing-guidelines)
- [Documentation](#documentation)
- [Community Guidelines](#community-guidelines)
- [Release Process](#release-process)
- [Support](#support)

---

## 🚀 Getting Started

### Prerequisites
- **.NET 6.0+**: For backend development
- **Node.js 16+**: For frontend development
- **PostgreSQL 12+**: For database development
- **Git**: For version control
- **Docker**: For containerized development (optional)

### Quick Start
```bash
# Clone the repository
git clone https://github.com/your-org/readarr-revival.git
cd readarr-revival

# Set up development environment
./scripts/setup-dev.sh

# Start the application
docker-compose up -d
```

---

## 🔧 Development Setup

### Backend Development (C#)
```bash
# Navigate to backend
cd src/

# Restore packages
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test

# Start the application
dotnet run --project NzbDrone.Host/Readarr.Host.csproj
```

### Frontend Development (React)
```bash
# Navigate to frontend
cd frontend/

# Install dependencies
npm install

# Start development server
npm start

# Run tests
npm test

# Build for production
npm run build
```

### Database Development
```bash
# Start PostgreSQL
docker-compose up postgres -d

# Run migrations
dotnet ef database update --project src/NzbDrone.Core

# Seed test data
dotnet run --project src/NzbDrone.Core --seed
```

---

## 📝 Code Standards

### C# Backend Standards

#### Naming Conventions
```csharp
// Classes and Interfaces
public class MetadataProviderManager { }
public interface IMetadataProvider { }

// Methods and Properties
public async Task<MetadataResult> GetMetadataAsync(string isbn) { }
public string ProviderName { get; set; }

// Private fields
private readonly ILogger<MetadataProviderManager> _logger;
private readonly ICacheService _cache;
```

#### Code Organization
```csharp
// File structure
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NzbDrone.Core.MetadataSource
{
    public class MetadataProviderManager : IMetadataProviderManager
    {
        // Private fields
        private readonly ILogger<MetadataProviderManager> _logger;
        
        // Constructor
        public MetadataProviderManager(ILogger<MetadataProviderManager> logger)
        {
            _logger = logger;
        }
        
        // Public methods
        public async Task<MetadataResult> GetMetadataAsync(string isbn)
        {
            // Implementation
        }
        
        // Private methods
        private async Task<MetadataResult> ProcessResultAsync(MetadataResult result)
        {
            // Implementation
        }
    }
}
```

#### Error Handling
```csharp
public async Task<MetadataResult> GetMetadataAsync(string isbn)
{
    try
    {
        // Implementation
        var result = await _provider.GetMetadataAsync(isbn);
        return result;
    }
    catch (HttpRequestException ex)
    {
        _logger.LogError(ex, "HTTP request failed for ISBN {Isbn}", isbn);
        throw new MetadataProviderException($"Failed to fetch metadata for {isbn}", ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error fetching metadata for ISBN {Isbn}", isbn);
        throw;
    }
}
```

### Frontend Standards (React/TypeScript)

#### Component Structure
```typescript
// Component file structure
import React, { useState, useEffect } from 'react';
import { useQuery } from 'react-query';
import { MetadataResult } from '../types';

interface MetadataProviderProps {
  isbn: string;
  onResult: (result: MetadataResult) => void;
}

export const MetadataProvider: React.FC<MetadataProviderProps> = ({ 
  isbn, 
  onResult 
}) => {
  const [loading, setLoading] = useState(false);
  
  const { data, error } = useQuery(
    ['metadata', isbn],
    () => fetchMetadata(isbn),
    { enabled: !!isbn }
  );
  
  useEffect(() => {
    if (data) {
      onResult(data);
    }
  }, [data, onResult]);
  
  if (loading) return <div>Loading...</div>;
  if (error) return <div>Error: {error.message}</div>;
  
  return (
    <div className="metadata-provider">
      {/* Component content */}
    </div>
  );
};
```

#### Styling Standards
```css
/* Use CSS modules or styled-components */
.metadata-provider {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 1rem;
  border-radius: 8px;
  background-color: var(--background-secondary);
}

.metadata-provider__title {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary);
}

.metadata-provider__content {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
}
```

---

## 🧪 Testing Guidelines

### Backend Testing (C#)

#### Unit Tests
```csharp
[Fact]
public async Task GetMetadataAsync_ValidIsbn_ReturnsMetadata()
{
    // Arrange
    var mockProvider = new Mock<IMetadataProvider>();
    var expectedResult = new MetadataResult { Title = "Test Book" };
    mockProvider.Setup(p => p.GetMetadataAsync("1234567890"))
                .ReturnsAsync(expectedResult);
    
    var manager = new MetadataProviderManager(mockProvider.Object);
    
    // Act
    var result = await manager.GetMetadataAsync("1234567890");
    
    // Assert
    Assert.NotNull(result);
    Assert.Equal("Test Book", result.Title);
    mockProvider.Verify(p => p.GetMetadataAsync("1234567890"), Times.Once);
}
```

#### Integration Tests
```csharp
[Fact]
public async Task MetadataProvider_Integration_WorksEndToEnd()
{
    // Arrange
    var services = new ServiceCollection()
        .AddLogging()
        .AddDbContext<ReadarrContext>(options => 
            options.UseInMemoryDatabase("test-db"))
        .AddScoped<IMetadataProviderManager, MetadataProviderManager>()
        .BuildServiceProvider();
    
    var manager = services.GetRequiredService<IMetadataProviderManager>();
    
    // Act
    var result = await manager.GetMetadataAsync("9780123456789");
    
    // Assert
    Assert.NotNull(result);
    Assert.True(result.ConfidenceScore > 0.5);
}
```

### Frontend Testing (React)

#### Component Tests
```typescript
import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from 'react-query';
import { MetadataProvider } from './MetadataProvider';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: false },
  },
});

test('renders metadata provider with valid ISBN', async () => {
  render(
    <QueryClientProvider client={queryClient}>
      <MetadataProvider isbn="1234567890" onResult={jest.fn()} />
    </QueryClientProvider>
  );
  
  await waitFor(() => {
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });
});
```

---

## 📚 Documentation

### Code Documentation
```csharp
/// <summary>
/// Manages metadata providers and coordinates metadata retrieval.
/// </summary>
/// <remarks>
/// This class implements a fallback system that tries multiple providers
/// in order of priority until a result with sufficient confidence is found.
/// </remarks>
public class MetadataProviderManager : IMetadataProviderManager
{
    /// <summary>
    /// Retrieves metadata for a given ISBN from available providers.
    /// </summary>
    /// <param name="isbn">The ISBN to search for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A metadata result with confidence scoring, or null if no suitable
    /// result is found from any provider.
    /// </returns>
    /// <exception cref="MetadataProviderException">
    /// Thrown when all providers fail to return valid metadata.
    /// </exception>
    public async Task<MetadataResult> GetMetadataAsync(
        string isbn, 
        CancellationToken cancellationToken = default)
    {
        // Implementation
    }
}
```

### API Documentation
```csharp
/// <summary>
/// Metadata API endpoints for book information retrieval.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class MetadataController : ControllerBase
{
    /// <summary>
    /// Retrieves metadata for a book by ISBN.
    /// </summary>
    /// <param name="isbn">The ISBN of the book</param>
    /// <returns>Book metadata with confidence scoring</returns>
    /// <response code="200">Metadata retrieved successfully</response>
    /// <response code="404">Book not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("{isbn}")]
    [ProducesResponseType(typeof(MetadataResult), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<MetadataResult>> GetMetadata(string isbn)
    {
        // Implementation
    }
}
```

---

## 👥 Community Guidelines

### Code of Conduct
We are committed to providing a welcoming and inclusive environment for all contributors. Please read our [Code of Conduct](CODE_OF_CONDUCT.md) before participating.

### Communication
- **GitHub Issues**: For bug reports and feature requests
- **Discord**: For community discussions and support
- **Pull Requests**: For code contributions
- **Wiki**: For documentation improvements

### Contribution Types
1. **Bug Fixes**: Fix issues and improve reliability
2. **Feature Development**: Add new functionality
3. **Documentation**: Improve guides and tutorials
4. **Testing**: Add tests and improve coverage
5. **Community Support**: Help other users and contributors

### Pull Request Process
1. **Fork** the repository
2. **Create** a feature branch (`git checkout -b feature/amazing-feature`)
3. **Commit** your changes (`git commit -m 'Add amazing feature'`)
4. **Push** to the branch (`git push origin feature/amazing-feature`)
5. **Open** a Pull Request

### Pull Request Guidelines
- **Title**: Clear, descriptive title
- **Description**: Detailed description of changes
- **Tests**: Include tests for new functionality
- **Documentation**: Update relevant documentation
- **Screenshots**: Include screenshots for UI changes

---

## 🚀 Release Process

### Versioning
We follow [Semantic Versioning](https://semver.org/):
- **MAJOR**: Breaking changes
- **MINOR**: New features (backward compatible)
- **PATCH**: Bug fixes (backward compatible)

### Release Checklist
- [ ] All tests passing
- [ ] Code coverage maintained
- [ ] Documentation updated
- [ ] Changelog updated
- [ ] Version bumped
- [ ] Release notes prepared
- [ ] Community announcement ready

### Release Steps
```bash
# Update version
dotnet version bump minor

# Run full test suite
dotnet test --configuration Release

# Build release artifacts
dotnet build --configuration Release

# Create release tag
git tag v1.2.0
git push origin v1.2.0

# Create GitHub release
gh release create v1.2.0 --notes-file CHANGELOG.md
```

---

## 🆘 Support

### Getting Help
- **Documentation**: Check our [Wiki](https://github.com/your-org/readarr-revival/wiki)
- **Issues**: Search existing [GitHub Issues](https://github.com/your-org/readarr-revival/issues)
- **Discord**: Join our [Community Server](https://discord.gg/readarr-revival)
- **Email**: Contact support@readarr-revival.org

### Development Resources
- **API Documentation**: [API Reference](https://readarr-revival.org/api)
- **Development Guide**: [Developer Wiki](https://github.com/your-org/readarr-revival/wiki/Development)
- **Architecture**: [System Design](https://github.com/your-org/readarr-revival/wiki/Architecture)
- **Testing Guide**: [Testing Documentation](https://github.com/your-org/readarr-revival/wiki/Testing)

---

## 🏆 Recognition

### Contributors
We recognize all contributors in our [Contributors List](https://github.com/your-org/readarr-revival/graphs/contributors).

### Hall of Fame
Special recognition for significant contributions:
- **Core Contributors**: Major feature development
- **Documentation Heroes**: Comprehensive documentation improvements
- **Bug Hunters**: Critical bug fixes and reliability improvements
- **Community Champions**: Outstanding community support

---

## 📄 License

By contributing to Readarr Revival, you agree that your contributions will be licensed under the [GNU GPL v3 License](LICENSE).

---

**Thank you for contributing to the Readarr Revival Project!** 🎉

Your contributions help make book management better for everyone in the community.
