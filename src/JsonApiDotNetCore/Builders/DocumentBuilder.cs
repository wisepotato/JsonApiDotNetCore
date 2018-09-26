using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JsonApiDotNetCore.Extensions;
using JsonApiDotNetCore.Internal;
using JsonApiDotNetCore.Models;
using JsonApiDotNetCore.Services;

namespace JsonApiDotNetCore.Builders
{
    public class DocumentBuilder : IDocumentBuilder
    {
        private readonly IJsonApiContext _jsonApiContext;
        private readonly IContextGraph _contextGraph;
        private readonly IRequestMeta _requestMeta;
        private readonly DocumentBuilderOptions _documentBuilderOptions;
        private readonly IScopedServiceProvider _scopedServiceProvider;

        public DocumentBuilder(
            IJsonApiContext jsonApiContext, 
            IRequestMeta requestMeta = null, 
            IDocumentBuilderOptionsProvider documentBuilderOptionsProvider = null,
            IScopedServiceProvider scopedServiceProvider = null)
        {
            _jsonApiContext = jsonApiContext;
            _contextGraph = jsonApiContext.ContextGraph;
            _requestMeta = requestMeta;
            _documentBuilderOptions = documentBuilderOptionsProvider?.GetDocumentBuilderOptions() ?? new DocumentBuilderOptions();
            _scopedServiceProvider = scopedServiceProvider;
        }

        public Document Build(IIdentifiable entity)
        {
            var contextEntity = _contextGraph.GetContextEntity(entity.GetType());

            var resourceDefinition = _scopedServiceProvider?.GetService(contextEntity.ResourceType) as IResourceDefinition;
            var document = new Document
            {
                Data = GetData(contextEntity, entity, resourceDefinition),
                Meta = GetMeta(entity)
            };

            if (ShouldIncludePageLinks(contextEntity))
                document.Links = _jsonApiContext.PageManager.GetPageLinks(new LinkBuilder(_jsonApiContext));

            document.Included = AppendIncludedObject(document.Included, contextEntity, entity);

            return document;
        }

        public Documents Build(IEnumerable<IIdentifiable> entities)
        {
            var entityType = entities.GetElementType();
            var contextEntity = _contextGraph.GetContextEntity(entityType);
            var resourceDefinition = _scopedServiceProvider?.GetService(contextEntity.ResourceType) as IResourceDefinition;

            var enumeratedEntities = entities as IList<IIdentifiable> ?? entities.ToList();
            var documents = new Documents
            {
                Data = new List<DocumentData>(),
                Meta = GetMeta(enumeratedEntities.FirstOrDefault())
            };

            if (ShouldIncludePageLinks(contextEntity))
                documents.Links = _jsonApiContext.PageManager.GetPageLinks(new LinkBuilder(_jsonApiContext));

            foreach (var entity in enumeratedEntities)
            {
                documents.Data.Add(GetData(contextEntity, entity, resourceDefinition));
                documents.Included = AppendIncludedObject(documents.Included, contextEntity, entity);
            }

            return documents;
        }

        private Dictionary<string, object> GetMeta(IIdentifiable entity)
        {
            var builder = _jsonApiContext.MetaBuilder;
            if (_jsonApiContext.Options.IncludeTotalRecordCount && _jsonApiContext.PageManager.TotalRecords != null)
                builder.Add("total-records", _jsonApiContext.PageManager.TotalRecords);

            if (_requestMeta != null)
                builder.Add(_requestMeta.GetMeta());

            if (entity != null && entity is IHasMeta metaEntity)
                builder.Add(metaEntity.GetMeta(_jsonApiContext));

            var meta = builder.Build();
            if (meta.Count > 0)
                return meta;

            return null;
        }

        private bool ShouldIncludePageLinks(ContextEntity entity) => entity.Links.HasFlag(Link.Paging);

        private List<DocumentData> AppendIncludedObject(List<DocumentData> includedObject, ContextEntity contextEntity, IIdentifiable entity)
        {
            var includedEntities = GetIncludedEntities(includedObject, contextEntity, entity);
            if (includedEntities?.Count > 0)
            {
                includedObject = includedEntities;
            }

            return includedObject;
        }

        [Obsolete("You should specify an IResourceDefinition implementation using the GetData/3 overload.")]
        public DocumentData GetData(ContextEntity contextEntity, IIdentifiable entity)
            => GetData(contextEntity, entity, resourceDefinition: null);

        public DocumentData GetData(ContextEntity contextEntity, IIdentifiable entity, IResourceDefinition resourceDefinition = null)
        {
            var data = new DocumentData
            {
                Type = contextEntity.EntityName,
                Id = entity.StringId
            };

            if (_jsonApiContext.IsRelationshipPath)
                return data;

            data.Attributes = new Dictionary<string, object>();

            var resourceAttributes = resourceDefinition?.GetOutputAttrs(entity) ?? contextEntity.Attributes;
            resourceAttributes.ForEach(attr =>
            {
                var attributeValue = attr.GetValue(entity);
                if (ShouldIncludeAttribute(attr, attributeValue))
                {
                    data.Attributes.Add(attr.PublicAttributeName, attributeValue);
                }
            });

            if (contextEntity.Relationships.Count > 0)
                AddRelationships(data, contextEntity, entity);

            return data;
        }
        private bool ShouldIncludeAttribute(AttrAttribute attr, object attributeValue)
        {
            return OmitNullValuedAttribute(attr, attributeValue) == false
                   && ((_jsonApiContext.QuerySet == null
                       || _jsonApiContext.QuerySet.Fields.Count == 0)
                       || _jsonApiContext.QuerySet.Fields.Contains(attr.InternalAttributeName));
        }

        private bool OmitNullValuedAttribute(AttrAttribute attr, object attributeValue)
        {
            return attributeValue == null && _documentBuilderOptions.OmitNullValuedAttributes;
        }

        private void AddRelationships(DocumentData data, ContextEntity contextEntity, IIdentifiable entity)
        {
            data.Relationships = new Dictionary<string, RelationshipData>();
            contextEntity.Relationships.ForEach(r =>
                data.Relationships.Add(
                    r.PublicRelationshipName,
                    GetRelationshipData(r, contextEntity, entity)
                )
            );
        }

        private RelationshipData GetRelationshipData(RelationshipAttribute attr, ContextEntity contextEntity, IIdentifiable entity)
        {
            var linkBuilder = new LinkBuilder(_jsonApiContext);

            var relationshipData = new RelationshipData();

            if (attr.DocumentLinks.HasFlag(Link.None) == false)
            {
                relationshipData.Links = new Links();
                if (attr.DocumentLinks.HasFlag(Link.Self))
                    relationshipData.Links.Self = linkBuilder.GetSelfRelationLink(contextEntity.EntityName, entity.StringId, attr.PublicRelationshipName);

                if (attr.DocumentLinks.HasFlag(Link.Related))
                    relationshipData.Links.Related = linkBuilder.GetRelatedRelationLink(contextEntity.EntityName, entity.StringId, attr.PublicRelationshipName);
            }

            // this only includes the navigation property, we need to actually check the navigation property Id
            var navigationEntity = _jsonApiContext.ContextGraph.GetRelationship(entity, attr.InternalRelationshipName);
            if (navigationEntity == null)
                relationshipData.SingleData = attr.IsHasOne
                    ? GetIndependentRelationshipIdentifier((HasOneAttribute)attr, entity)
                    : null;
            else if (navigationEntity is IEnumerable)
                relationshipData.ManyData = GetRelationships((IEnumerable<object>)navigationEntity);
            else
                relationshipData.SingleData = GetRelationship(navigationEntity);

            return relationshipData;
        }

        private List<DocumentData> GetIncludedEntities(List<DocumentData> included, ContextEntity rootContextEntity, IIdentifiable rootResource)
        {
            if(_jsonApiContext.IncludedRelationships != null)
            {
                foreach(var relationshipName in _jsonApiContext.IncludedRelationships)
                {
                    var relationshipChain = relationshipName.Split('.');

                    var contextEntity = rootContextEntity;
                    var entity = rootResource;
                    included = IncludeRelationshipChain(included, rootContextEntity, rootResource, relationshipChain, 0);
                }                
            }

            return included;
        }

        private List<DocumentData> IncludeRelationshipChain(
            List<DocumentData> included, ContextEntity parentEntity, IIdentifiable parentResource, string[] relationshipChain, int relationshipChainIndex)
        {
            var requestedRelationship = relationshipChain[relationshipChainIndex];
            var relationship = parentEntity.Relationships.FirstOrDefault(r => r.PublicRelationshipName == requestedRelationship);
            var navigationEntity = _jsonApiContext.ContextGraph.GetRelationship(parentResource, relationship.InternalRelationshipName);
            if (navigationEntity is IEnumerable hasManyNavigationEntity)
            {
                foreach (IIdentifiable includedEntity in hasManyNavigationEntity)
                {
                    included = AddIncludedEntity(included, includedEntity);
                    included = IncludeSingleResourceRelationships(included, includedEntity, relationship, relationshipChain, relationshipChainIndex);
                }
            }
            else
            {
                included = AddIncludedEntity(included, (IIdentifiable)navigationEntity);
                included = IncludeSingleResourceRelationships(included, (IIdentifiable)navigationEntity, relationship, relationshipChain, relationshipChainIndex);
            }

            return included;
        }

        private List<DocumentData> IncludeSingleResourceRelationships(
            List<DocumentData> included, IIdentifiable navigationEntity, RelationshipAttribute relationship, string[] relationshipChain, int relationshipChainIndex)
        {
            if(relationshipChainIndex < relationshipChain.Length) 
            {
                var nextContextEntity = _jsonApiContext.ContextGraph.GetContextEntity(relationship.Type);
                var resource = (IIdentifiable)navigationEntity;
                // recursive call
                if(relationshipChainIndex < relationshipChain.Length - 1)
                    included = IncludeRelationshipChain(included, nextContextEntity, resource, relationshipChain, relationshipChainIndex + 1);
            }
            
            return included;
        }


        private List<DocumentData> AddIncludedEntity(List<DocumentData> entities, IIdentifiable entity)
        {
            var includedEntity = GetIncludedEntity(entity);

            if (entities == null)
                entities = new List<DocumentData>();

            if (includedEntity != null && entities.Any(doc =>
                string.Equals(doc.Id, includedEntity.Id) && string.Equals(doc.Type, includedEntity.Type)) == false)
            {
                entities.Add(includedEntity);
            }

            return entities;
        }

        private DocumentData GetIncludedEntity(IIdentifiable entity)
        {
            if (entity == null) return null;

            var contextEntity = _jsonApiContext.ContextGraph.GetContextEntity(entity.GetType());
            var resourceDefinition = _scopedServiceProvider.GetService(contextEntity.ResourceType) as IResourceDefinition;

            var data = GetData(contextEntity, entity, resourceDefinition);

            data.Attributes = new Dictionary<string, object>();

            contextEntity.Attributes.ForEach(attr =>
            {
                data.Attributes.Add(attr.PublicAttributeName, attr.GetValue(entity));
            });

            return data;
        }

        private List<ResourceIdentifierObject> GetRelationships(IEnumerable<object> entities)
        {
            var objType = entities.GetElementType();

            var typeName = _jsonApiContext.ContextGraph.GetContextEntity(objType);

            if (typeName == null)
            {
                throw new Exception($"You need to register the entity  {objType} to the context graph, to be able to load this relationship");
            }
            var relationships = new List<ResourceIdentifierObject>();
            foreach (var entity in entities)
            {
                relationships.Add(new ResourceIdentifierObject
                {
                    Type = typeName.EntityName,
                    Id = ((IIdentifiable)entity).StringId
                });
            }
            return relationships;
        }

        private ResourceIdentifierObject GetRelationship(object entity)
        {
            var objType = entity.GetType();
            var contextEntity = _jsonApiContext.ContextGraph.GetContextEntity(objType);

            if(entity is IIdentifiable identifiableEntity)
                return new ResourceIdentifierObject
                {
                    Type = contextEntity.EntityName,
                    Id = identifiableEntity.StringId
                };

            return null;
        }

        private ResourceIdentifierObject GetIndependentRelationshipIdentifier(HasOneAttribute hasOne, IIdentifiable entity)
        {
            var independentRelationshipIdentifier = hasOne.GetIdentifiablePropertyValue(entity);
            if (independentRelationshipIdentifier == null)
                return null;

            var relatedContextEntity = _jsonApiContext.ContextGraph.GetContextEntity(hasOne.Type);
            if (relatedContextEntity == null) // TODO: this should probably be a debug log at minimum
                return null;

            return new ResourceIdentifierObject
            {
                Type = relatedContextEntity.EntityName,
                Id = independentRelationshipIdentifier.ToString()
            };
        }
    }
}
