using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Datastore.Migration.Framework.Interfaces;

namespace NzbDrone.Core.Datastore.Migrations
{
    [Migration(20240115001)]
    public class AddMetadataDatabase : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Create metadata_books table
            Create.TableForModel("MetadataBooks")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("GoodreadsId").AsString().Nullable()
                .WithColumn("Isbn13").AsString().WithLength(13).Nullable()
                .WithColumn("Isbn10").AsString().WithLength(10).Nullable()
                .WithColumn("Title").AsString().WithLength(500).NotNullable()
                .WithColumn("Subtitle").AsString().WithLength(500).Nullable()
                .WithColumn("Description").AsString().Nullable()
                .WithColumn("PublicationDate").AsDateTime().Nullable()
                .WithColumn("PageCount").AsInt32().Nullable()
                .WithColumn("Language").AsString().WithLength(10).Nullable()
                .WithColumn("CreatedAt").AsDateTime().NotNullable()
                .WithColumn("UpdatedAt").AsDateTime().NotNullable()
                .WithColumn("ConfidenceScore").AsDecimal().WithPrecision(3, 2).Nullable()
                .WithColumn("SourceData").AsString().Nullable(); // JSON data from sources

            // Create metadata_authors table
            Create.TableForModel("MetadataAuthors")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("GoodreadsId").AsString().Nullable()
                .WithColumn("Name").AsString().WithLength(200).NotNullable()
                .WithColumn("Biography").AsString().Nullable()
                .WithColumn("BirthDate").AsDateTime().Nullable()
                .WithColumn("DeathDate").AsDateTime().Nullable()
                .WithColumn("CreatedAt").AsDateTime().NotNullable()
                .WithColumn("UpdatedAt").AsDateTime().NotNullable()
                .WithColumn("ConfidenceScore").AsDecimal().WithPrecision(3, 2).Nullable()
                .WithColumn("SourceData").AsString().Nullable(); // JSON data from sources

            // Create metadata_sources table
            Create.TableForModel("MetadataSources")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("BookId").AsInt32().ForeignKey("MetadataBooks", "Id").Nullable()
                .WithColumn("AuthorId").AsInt32().ForeignKey("MetadataAuthors", "Id").Nullable()
                .WithColumn("SourceName").AsString().WithLength(50).NotNullable()
                .WithColumn("SourceData").AsString().Nullable() // JSON data from source
                .WithColumn("RetrievedAt").AsDateTime().NotNullable()
                .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true);

            // Create metadata_series table
            Create.TableForModel("MetadataSeries")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("GoodreadsId").AsString().Nullable()
                .WithColumn("Title").AsString().WithLength(200).NotNullable()
                .WithColumn("Description").AsString().Nullable()
                .WithColumn("CreatedAt").AsDateTime().NotNullable()
                .WithColumn("UpdatedAt").AsDateTime().NotNullable()
                .WithColumn("ConfidenceScore").AsDecimal().WithPrecision(3, 2).Nullable();

            // Create metadata_series_books table (many-to-many relationship)
            Create.TableForModel("MetadataSeriesBooks")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("SeriesId").AsInt32().ForeignKey("MetadataSeries", "Id").NotNullable()
                .WithColumn("BookId").AsInt32().ForeignKey("MetadataBooks", "Id").NotNullable()
                .WithColumn("Position").AsInt32().Nullable()
                .WithColumn("IsPrimary").AsBoolean().NotNullable().WithDefaultValue(false);

            // Add indexes for performance
            Create.Index().OnTable("MetadataBooks").OnColumn("GoodreadsId");
            Create.Index().OnTable("MetadataBooks").OnColumn("Isbn13");
            Create.Index().OnTable("MetadataBooks").OnColumn("Isbn10");
            Create.Index().OnTable("MetadataBooks").OnColumn("Title");
            Create.Index().OnTable("MetadataBooks").OnColumn("ConfidenceScore");
            Create.Index().OnTable("MetadataBooks").OnColumn("CreatedAt");

            Create.Index().OnTable("MetadataAuthors").OnColumn("GoodreadsId");
            Create.Index().OnTable("MetadataAuthors").OnColumn("Name");
            Create.Index().OnTable("MetadataAuthors").OnColumn("ConfidenceScore");

            Create.Index().OnTable("MetadataSources").OnColumn("BookId");
            Create.Index().OnTable("MetadataSources").OnColumn("AuthorId");
            Create.Index().OnTable("MetadataSources").OnColumn("SourceName");
            Create.Index().OnTable("MetadataSources").OnColumn("RetrievedAt");

            Create.Index().OnTable("MetadataSeries").OnColumn("GoodreadsId");
            Create.Index().OnTable("MetadataSeries").OnColumn("Title");

            Create.Index().OnTable("MetadataSeriesBooks").OnColumn("SeriesId");
            Create.Index().OnTable("MetadataSeriesBooks").OnColumn("BookId");

            // Add metadata columns to existing Books table
            Alter.Table("Books").AddColumn("MetadataConfidence").AsDecimal().WithPrecision(3, 2).Nullable();
            Alter.Table("Books").AddColumn("MetadataSources").AsString().Nullable(); // JSON array of source names
            Alter.Table("Books").AddColumn("LastMetadataUpdate").AsDateTime().Nullable();

            // Add metadata columns to existing Authors table
            Alter.Table("Authors").AddColumn("MetadataConfidence").AsDecimal().WithPrecision(3, 2).Nullable();
            Alter.Table("Authors").AddColumn("MetadataSources").AsString().Nullable(); // JSON array of source names
            Alter.Table("Authors").AddColumn("LastMetadataUpdate").AsDateTime().Nullable();

            // Create indexes on new columns
            Create.Index().OnTable("Books").OnColumn("MetadataConfidence");
            Create.Index().OnTable("Books").OnColumn("LastMetadataUpdate");
            Create.Index().OnTable("Authors").OnColumn("MetadataConfidence");
            Create.Index().OnTable("Authors").OnColumn("LastMetadataUpdate");
        }
    }
} 