﻿using Examine.LuceneEngine.Providers;
using Lucene.Net.Documents;
using Lucene.Net.Search;
using Our.Umbraco.Look.Extensions;
using Our.Umbraco.Look.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Umbraco.Core.Models;

namespace Our.Umbraco.Look.Services
{
    public partial class LookService
    {
        /// <summary>
        /// Supplied with the result of a Lucene query, this method will yield a constructed LookMatch for each in order
        /// </summary>
        /// <param name="indexSearcher">The searcher supplied to get the Lucene doc for each id in the Lucene results (topDocs)</param>
        /// <param name="topDocs">The results of the Lucene query (a collection of ids in an order)</param>
        /// <param name="getHighlight">Function used to get the highlight text for a given result text</param>
        /// <param name="getText">Flag to indidicate whether the full value of the text field should be returned</param>
        /// <param name="getDistance">Function used to calculate distance (if a location was supplied in the original query)</param>
        /// <returns></returns>
        private static IEnumerable<LookMatch> GetLookMatches(
                                                    IndexSearcher indexSearcher,
                                                    TopDocs topDocs,
                                                    Func<string, IHtmlString> getHighlight,
                                                    bool getText,
                                                    Func<int, double?> getDistance)
        {
            var fields = new List<string>();

            fields.Add(LuceneIndexer.IndexNodeIdFieldName); // "__NodeId"
            fields.Add(LookConstants.NodeTypeField);
            fields.Add(LookConstants.NameField);
            fields.Add(LookConstants.DateField);

            // if a highlight function is supplied (or text requested)
            if (getHighlight != null || getText) { fields.Add(LookConstants.TextField); }

            fields.Add(LookConstants.AllTagsField); // single field used to store all tags (for quick re-construction)
            fields.Add(LookConstants.LocationField);

            var mapFieldSelector = new MapFieldSelector(fields.ToArray());

            // there should always be a valid node type value to parse
            var getNodeType = new Func<string, NodeType>(x => { Enum.TryParse(x, out NodeType nodeType); return nodeType; });

            // if highlight func does not exist, then create one to always return null
            if (getHighlight == null) { getHighlight = x => null; }
            
            // helper to simplify call below
            var getTags = new Func<Field[], LookTag[]>(x => {
                if (x != null) { return x.Select(y => new LookTag(y.StringValue())).ToArray(); }
                return new LookTag[] { };
            });

            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var docId = scoreDoc.doc;

                var doc = indexSearcher.Doc(docId, mapFieldSelector);

                var lookMatch = new LookMatch(
                    LookService.Instance.UmbracoHelper,
                    Convert.ToInt32(doc.Get(LuceneIndexer.IndexNodeIdFieldName)),
                    getNodeType(doc.Get(LookConstants.NodeTypeField)),
                    getHighlight(doc.Get(LookConstants.TextField)),
                    getText ? doc.Get(LookConstants.TextField) : null,
                    getTags(doc.GetFields(LookConstants.AllTagsField)),
                    doc.Get(LookConstants.DateField).LuceneStringToDate(),
                    doc.Get(LookConstants.NameField),
                    doc.Get(LookConstants.LocationField) != null ? Location.FromString(doc.Get(LookConstants.LocationField)) : null,
                    getDistance(docId)
                );
                
                yield return lookMatch;
            }
        }
    }
}
