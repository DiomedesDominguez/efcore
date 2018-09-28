﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Cosmos.Sql.Metadata.Conventions.Internal;
using Microsoft.EntityFrameworkCore.Cosmos.Sql.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Cosmos.Sql.Storage.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;
using Newtonsoft.Json.Linq;

namespace Microsoft.EntityFrameworkCore.Cosmos.Sql.Update.Internal
{
    public class DocumentSource
    {
        private readonly string _collectionId;
        private readonly IEntityType _entityType;
        private readonly CosmosSqlDatabase _database;

        public DocumentSource(IEntityType entityType, CosmosSqlDatabase database)
        {
            _collectionId = entityType.CosmosSql().CollectionName;
            _entityType = entityType;
            _database = database;
        }

        public string GetCollectionId()
            => _collectionId;

        public string GetId(IUpdateEntry entry)
            => entry.GetCurrentValue<string>(_entityType.FindProperty(StoreKeyConvention.IdPropertyName));

        public JObject CreateDocument(IUpdateEntry entry)
        {
            var document = new JObject();
            foreach (var property in _entityType.GetProperties())
            {
                if (property.Name != StoreKeyConvention.JObjectPropertyName)
                {
                    var value = entry.GetCurrentValue(property);
                    document[property.Name] = value != null ? JToken.FromObject(value) : null;
                }
            }

            foreach (var ownedNavigation in _entityType.GetNavigations())
            {
                var fk = ownedNavigation.ForeignKey;
                if (!fk.IsOwnership
                    || ownedNavigation.IsDependentToPrincipal()
                    || fk.DeclaringEntityType.IsDocumentRoot())
                {
                    continue;
                }

                var nestedValue = entry.GetCurrentValue(ownedNavigation);
                if (nestedValue == null)
                {
                    document[ownedNavigation.Name] = null;
                }

                if (fk.IsUnique)
                {
                    var dependentEntry = ((InternalEntityEntry)entry).StateManager.TryGetEntry(nestedValue, fk.DeclaringEntityType);
                    document[ownedNavigation.Name] = _database.GetDocumentSource(dependentEntry.EntityType).CreateDocument(dependentEntry);
                }
                else
                {
                    var array = new JArray();
                    foreach (var dependent in (IEnumerable)nestedValue)
                    {
                        var dependentEntry = ((InternalEntityEntry)entry).StateManager.TryGetEntry(dependent, fk.DeclaringEntityType);
                        array.Add(_database.GetDocumentSource(dependentEntry.EntityType).CreateDocument(dependentEntry));
                    }

                    document[ownedNavigation.Name] = array;
                }
            }

            return document;
        }

        public JObject UpdateDocument(JObject document, IUpdateEntry entry)
        {
            foreach (var property in _entityType.GetProperties())
            {
                if (property.Name != StoreKeyConvention.JObjectPropertyName
                    && entry.IsModified(property))
                {
                    var value = entry.GetCurrentValue(property);
                    document[property.Name] = value != null ? JToken.FromObject(value) : null;
                }
            }

            foreach (var ownedNavigation in _entityType.GetNavigations())
            {
                var fk = ownedNavigation.ForeignKey;
                if (!fk.IsOwnership
                    || ownedNavigation.IsDependentToPrincipal()
                    || fk.DeclaringEntityType.IsDocumentRoot())
                {
                    continue;
                }

                var nestedDocumentSource = _database.GetDocumentSource(fk.DeclaringEntityType);
                var nestedValue = entry.GetCurrentValue(ownedNavigation);
                if (nestedValue == null)
                {
                    document[ownedNavigation.Name] = null;
                }

                if (fk.IsUnique)
                {
                    var nestedEntry = ((InternalEntityEntry)entry).StateManager.TryGetEntry(nestedValue, fk.DeclaringEntityType);
                    var nestedDocument = (JObject)document[ownedNavigation.Name];
                    if (nestedDocument != null)
                    {
                        nestedDocumentSource.UpdateDocument(nestedDocument, nestedEntry);
                    }
                    else
                    {
                        nestedDocument = nestedDocumentSource.CreateDocument(nestedEntry);
                    }

                    document[ownedNavigation.Name] = nestedDocument;
                }
                else
                {
                    var array = new JArray();
                    foreach (var dependent in (IEnumerable)nestedValue)
                    {
                        var dependentEntry = ((InternalEntityEntry)entry).StateManager.TryGetEntry(dependent, fk.DeclaringEntityType);
                        array.Add(_database.GetDocumentSource(dependentEntry.EntityType).CreateDocument(dependentEntry));
                    }

                    document[ownedNavigation.Name] = array;
                }
            }

            return document;
        }
    }
}
