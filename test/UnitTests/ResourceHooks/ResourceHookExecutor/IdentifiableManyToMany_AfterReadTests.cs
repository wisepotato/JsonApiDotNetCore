using JsonApiDotNetCore.Builders;
using JsonApiDotNetCore.Services;
using JsonApiDotNetCoreExample.Models;
using Moq;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Bogus;

namespace UnitTests.ResourceHooks
{

    public class IdentifiableManyToMany_AfterReadTests : ResourceHooksTestBase
    {
        public IdentifiableManyToMany_AfterReadTests()
        {
            // Build() exposes the static ResourceGraphBuilder.Instance member, which 
            // is consumed by ResourceDefinition class.
            new ResourceGraphBuilder()
                .AddResource<Article>()
                .AddResource<IdentifiableArticleTag>()
                .AddResource<Tag>()
                .Build();
        }

        (List<Article>, List<IdentifiableArticleTag>, List<Tag>) CreateDummyData()
        {
            var tagsSubset = _tagFaker.Generate(3).ToList();
            var joinsSubSet = _identifiableArticleTagFaker.Generate(3).ToList();
            var articleTagsSubset = _articleFaker.Generate();
            articleTagsSubset.IdentifiableArticleTags = joinsSubSet;
            for (int i = 0; i < 3; i++)
            {
                joinsSubSet[i].Article = articleTagsSubset;
                joinsSubSet[i].Tag = tagsSubset[i];
            }
            var allTags = _tagFaker.Generate(3).ToList().Concat(tagsSubset).ToList();
            var completeJoin = _identifiableArticleTagFaker.Generate(6).ToList();

            var articleWithAllTags = _articleFaker.Generate();
            articleWithAllTags.IdentifiableArticleTags = joinsSubSet;

            for (int i = 0; i < 6; i++)
            {
                completeJoin[i].Article = articleWithAllTags;
                completeJoin[i].Tag = allTags[i];
            }

            var allJoins = joinsSubSet.Concat(completeJoin).ToList();
            var articles = new List<Article>() { articleTagsSubset, articleWithAllTags };
            return (articles, allJoins, allTags);
        }


        [Fact]
        public void AfterRead()
        {
            // arrange
            var articleDiscovery = SetDiscoverableHooks<Article>();
            var joinDiscovery = SetDiscoverableHooks<IdentifiableArticleTag>();
            var tagDiscovery = SetDiscoverableHooks<Tag>();

            (var contextMock, var hookExecutor, var articleResourceMock,
                var joinResourceMock, var tagResourceMock) = CreateTestObjects(articleDiscovery, joinDiscovery, tagDiscovery);

            (var articles, var joins, var tags) = CreateDummyData();

            // act
            hookExecutor.AfterRead(articles, ResourceAction.Get);

            // assert
            articleResourceMock.Verify(rd => rd.AfterRead(articles, ResourceAction.Get, false), Times.Once());
            joinResourceMock.Verify(rd => rd.AfterRead(It.Is<IEnumerable<IdentifiableArticleTag>>((collection) => !collection.Except(joins).Any()), ResourceAction.Get, true), Times.Once());
            tagResourceMock.Verify(rd => rd.AfterRead(It.Is<IEnumerable<Tag>>((collection) => !collection.Except(tags).Any()), ResourceAction.Get, true), Times.Once());
            VerifyNoOtherCalls(articleResourceMock, joinResourceMock, tagResourceMock);
        }

        [Fact]
        public void AfterRead_Without_Parent_Hook_Implemented()
        {
            // arrange
            var articleDiscovery = SetDiscoverableHooks<Article>(new ResourceHook[0]);
            var joinDiscovery = SetDiscoverableHooks<IdentifiableArticleTag>();
            var tagDiscovery = SetDiscoverableHooks<Tag>();

            (var contextMock, var hookExecutor, var articleResourceMock,
                var joinResourceMock, var tagResourceMock) = CreateTestObjects(articleDiscovery, joinDiscovery, tagDiscovery);

            (var articles, var joins, var tags) = CreateDummyData();

            // act
            hookExecutor.AfterRead(articles, ResourceAction.Get);

            // assert
            joinResourceMock.Verify(rd => rd.AfterRead(It.Is<IEnumerable<IdentifiableArticleTag>>((collection) => !collection.Except(joins).Any()), ResourceAction.Get, true), Times.Once());
            tagResourceMock.Verify(rd => rd.AfterRead(It.Is<IEnumerable<Tag>>((collection) => !collection.Except(tags).Any()), ResourceAction.Get, true), Times.Once());
            VerifyNoOtherCalls(articleResourceMock, joinResourceMock, tagResourceMock);
        }

        [Fact]
        public void AfterRead_Without_Children_After_Hooks_Implemented()
        {
            // arrange
            var articleDiscovery = SetDiscoverableHooks<Article>();
            var joinDiscovery = SetDiscoverableHooks<IdentifiableArticleTag>(new ResourceHook[0]);
            var tagDiscovery = SetDiscoverableHooks<Tag>(new ResourceHook[0]);

            (var contextMock, var hookExecutor, var articleResourceMock,
                var joinResourceMock, var tagResourceMock) = CreateTestObjects(articleDiscovery, joinDiscovery, tagDiscovery);

            (var articles, var joins, var tags) = CreateDummyData();

            // act
            hookExecutor.AfterRead(articles, ResourceAction.Get);

            // assert
            articleResourceMock.Verify(rd => rd.AfterRead(articles, ResourceAction.Get, false), Times.Once());
            VerifyNoOtherCalls(articleResourceMock, joinResourceMock, tagResourceMock);
        }

        [Fact]
        public void AfterRead_Without_Any_Children_Hooks_Implemented()
        {
            // arrange
            var articleDiscovery = SetDiscoverableHooks<Article>();
            var joinDiscovery = SetDiscoverableHooks<IdentifiableArticleTag>(new ResourceHook[0]);
            var tagDiscovery = SetDiscoverableHooks<Tag>(new ResourceHook[0]);

            (var contextMock, var hookExecutor, var articleResourceMock,
                var joinResourceMock, var tagResourceMock) = CreateTestObjects(articleDiscovery, joinDiscovery, tagDiscovery);

            (var articles, var joins, var tags) = CreateDummyData();

            // act
            hookExecutor.AfterRead(articles, ResourceAction.Get);

            // assert
            articleResourceMock.Verify(rd => rd.AfterRead(articles, ResourceAction.Get, false), Times.Once());
            VerifyNoOtherCalls(articleResourceMock, joinResourceMock, tagResourceMock);
        }

        [Fact]
        public void AfterRead_Without_Any_Hook_Implemented()
        {
            // arrange
            var articleDiscovery = SetDiscoverableHooks<Article>(new ResourceHook[0]);
            var joinDiscovery = SetDiscoverableHooks<IdentifiableArticleTag>(new ResourceHook[0]);
            var tagDiscovery = SetDiscoverableHooks<Tag>(new ResourceHook[0]);

            (var contextMock, var hookExecutor, var articleResourceMock,
                var joinResourceMock, var tagResourceMock) = CreateTestObjects(articleDiscovery, joinDiscovery, tagDiscovery);

            (var articles, var joins, var tags) = CreateDummyData();

            // act
            hookExecutor.AfterRead(articles, ResourceAction.Get);

            // asert
            VerifyNoOtherCalls(articleResourceMock, joinResourceMock, tagResourceMock);
        }
    }
}
