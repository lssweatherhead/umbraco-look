﻿using Lucene.Net.Documents;
using System;
using System.Linq;
using Umbraco.Core.Logging;

namespace Our.Umbraco.Look.Services
{
    internal partial class LookService
    {
        private static void IndexTags(IndexingContext indexingContext, Document document)
        {
            if (indexingContext.Cancelled) return;

            var tagIndexer = LookService.GetTagIndexer(indexingContext.IndexerName);

            if (tagIndexer != null)
            {
                LookTag[] tags = null;

                try
                {
                    tags = tagIndexer(indexingContext);
                }
                catch (Exception exception)
                {
                    LogHelper.WarnWithException(typeof(LookService), "Error in tag indexer", exception);
                }

                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        var hasTagsField = new Field(
                                                LookConstants.HasTagsField,
                                                "1",
                                                Field.Store.NO,
                                                Field.Index.NOT_ANALYZED);

                        // add all tags to a common field (serialized such that Tag objects can be restored from this)
                        var allTagsField = new Field(
                                            LookConstants.AllTagsField,
                                            tag.ToString(),
                                            Field.Store.YES,
                                            Field.Index.NOT_ANALYZED);

                        // add the tag value to a specific field - this is used for searching on
                        var tagField = new Field(
                                            LookConstants.TagsField + tag.Group,
                                            tag.Name,
                                            Field.Store.YES,
                                            Field.Index.NOT_ANALYZED);

                        document.Add(hasTagsField);
                        document.Add(allTagsField);
                        document.Add(tagField);
                    }

                    var tagGroups = tags.Select(x => x.Group).Distinct();

                    foreach (var tagGroup in tagGroups)
                    {
                        var tagGroupField = new Field(
                                                LookConstants.TagGroupField + tagGroup,
                                                "1",
                                                Field.Store.NO,
                                                Field.Index.NOT_ANALYZED);

                        document.Add(tagGroupField);
                    }
                }
            }
        }
    }
}
