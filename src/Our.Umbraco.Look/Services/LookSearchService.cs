﻿using Examine.LuceneEngine.Providers;
using Examine.LuceneEngine.SearchCriteria;
using Lucene.Net.Documents;
using Lucene.Net.Highlight;
using Lucene.Net.QueryParsers;
using Lucene.Net.Search;
using Lucene.Net.Spatial.Tier;
using Lucene.Net.Spatial.Tier.Projectors;
using Our.Umbraco.Look.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using Umbraco.Core.Logging;
using UmbracoExamine;

namespace Our.Umbraco.Look.Services
{
    public static class LookSearchService
    {
        /// <summary>
        ///  Main searching method
        /// </summary>
        /// <param name="lookQuery"></param>
        /// <returns>an IEnumerableWithTotal</returns>
        public static LookResult Query(LookQuery lookQuery)
        {
            if (lookQuery == null)
            {
                LogHelper.Info(typeof(LookService), "Supplied search query was null");

                return LookResult.Empty;
            }

            var searchProvider = LookService.Searcher;

            var searchCriteria = searchProvider.CreateSearchCriteria();

            var examineQuery = searchCriteria.Field(string.Empty, string.Empty);

            // Text
            if (lookQuery.TextQuery != null)
            {
                if (!string.IsNullOrWhiteSpace(lookQuery.TextQuery.SearchText))
                {
                    if (lookQuery.TextQuery.Fuzzyness > 0)
                    {
                        examineQuery.And().Field(LookConstants.TextField, lookQuery.TextQuery.SearchText.Fuzzy(lookQuery.TextQuery.Fuzzyness));
                    }
                    else
                    {
                        examineQuery.And().Field(LookConstants.TextField, lookQuery.TextQuery.SearchText);
                    }
                }
            }

            // Tags
            if (lookQuery.TagQuery != null)
            {
                if (lookQuery.TagQuery.AllTags != null)
                {
                    examineQuery.And().GroupedAnd(
                                    lookQuery.TagQuery.AllTags.Select(x => LookConstants.TagsField), 
                                    lookQuery.TagQuery.AllTags);
                }

                if (lookQuery.TagQuery.AnyTags != null)
                {
                    examineQuery.And().GroupedOr(
                                    lookQuery.TagQuery.AnyTags.Select(x => LookConstants.TagsField),
                                    lookQuery.TagQuery.AnyTags);
                }
            }

            // Date
            if (lookQuery.DateQuery != null && (lookQuery.DateQuery.After.HasValue || lookQuery.DateQuery.Before.HasValue))
            {
                examineQuery.And().Range(
                                LookConstants.DateField,
                                lookQuery.DateQuery.After.HasValue ? lookQuery.DateQuery.After.Value : DateTime.MinValue,
                                lookQuery.DateQuery.Before.HasValue ? lookQuery.DateQuery.Before.Value : DateTime.MaxValue);
            }

            //// Name
            //if (lookQuery.NameQuery != null)
            //{
            // StartsWith
            // Contains
            //}

            // Nodes
            if (lookQuery.NodeQuery != null)
            {
                if (lookQuery.NodeQuery.TypeAliases != null)
                {
                    var typeAliases = new List<string>();

                    typeAliases.AddRange(lookQuery.NodeQuery.TypeAliases);
                    typeAliases.RemoveAll(x => string.IsNullOrWhiteSpace(x));

                    if (typeAliases.Any())
                    {
                        examineQuery.And().GroupedOr(typeAliases.Select(x => UmbracoContentIndexer.NodeTypeAliasFieldName), typeAliases.ToArray());
                    }
                }

                if (lookQuery.NodeQuery.ExcludeIds != null)
                {
                    foreach (var excudeId in lookQuery.NodeQuery.ExcludeIds.Distinct())
                    {
                        examineQuery.Not().Id(excudeId);
                    }
                }
            }

            try
            {
                searchCriteria = examineQuery.Compile();
            }
            catch (Exception exception)
            {
                LogHelper.WarnWithException(typeof(LookService), "Could not compile the Examine query", exception);
            }

            if (searchCriteria != null && searchCriteria is LuceneSearchCriteria)
            {
                Sort sort = null;
                Filter filter = null;

                Func<int, double?> getDistance = x => null;
                Func<string, IHtmlString> getHighlight = null;

                switch (lookQuery.SortOn)
                {
                    case SortOn.Name: // a -> z
                        sort = new Sort(new SortField(LuceneIndexer.SortedFieldNamePrefix + LookConstants.NameField, SortField.STRING));
                        break;

                    case SortOn.DateAscending: // oldest -> newest
                        sort = new Sort(new SortField(LuceneIndexer.SortedFieldNamePrefix + LookConstants.DateField, SortField.LONG, false));
                        break;

                    case SortOn.DateDescending: // newest -> oldest
                        sort = new Sort(new SortField(LuceneIndexer.SortedFieldNamePrefix + LookConstants.DateField, SortField.LONG, true));
                        break;
                }

                if (lookQuery.LocationQuery != null && lookQuery.LocationQuery.Location != null)
                {
                    double maxDistance = LookService.MaxDistance;

                    if (lookQuery.LocationQuery.MaxDistance != null)
                    {
                        maxDistance = Math.Min(lookQuery.LocationQuery.MaxDistance.GetMiles(), maxDistance);
                    }

                    var distanceQueryBuilder = new DistanceQueryBuilder(
                                                lookQuery.LocationQuery.Location.Latitude,
                                                lookQuery.LocationQuery.Location.Longitude,
                                                maxDistance,
                                                LookConstants.LocationField + "_Latitude",
                                                LookConstants.LocationField + "_Longitude",
                                                CartesianTierPlotter.DefaltFieldPrefix,
                                                true);

                    // update filter
                    filter = distanceQueryBuilder.Filter;

                    if (lookQuery.SortOn == SortOn.Distance)
                    {
                        // update sort
                        sort = new Sort(
                                    new SortField(
                                        LookConstants.DistanceField,
                                        new DistanceFieldComparatorSource(distanceQueryBuilder.DistanceFilter)));
                    }

                    // raw data for the getDistance func
                    var distances = distanceQueryBuilder.DistanceFilter.Distances;

                    // update getDistance func
                    getDistance = new Func<int, double?>(x =>
                    {
                        if (distances.ContainsKey(x))
                        {
                            return distances[x];
                        }

                        return null;
                    });
                }

                var indexSearcher = new IndexSearcher(((LuceneIndexer)LookService.Indexer).GetLuceneDirectory(), true);

                var luceneSearchCriteria = (LuceneSearchCriteria)searchCriteria;

                // do the Lucene search
                var topDocs = indexSearcher.Search(
                                            luceneSearchCriteria.Query, // the query build by Examine
                                            filter,
                                            LookService.MaxLuceneResults,
                                            sort ?? new Sort(SortField.FIELD_SCORE));

                if (topDocs.TotalHits > 0)
                {
                    Facet[] facets = null;

                    // facets
                    if (lookQuery.TagQuery != null && lookQuery.TagQuery.GetFacets)
                    {
                        var simpleFacetedSearch = new SimpleFacetedSearch(indexSearcher.GetIndexReader(), LookConstants.TagsField);

                        Query facetQuery = null;

                        if (filter != null)
                        {
                            facetQuery = new FilteredQuery(luceneSearchCriteria.Query, filter);
                        }
                        else
                        {
                            facetQuery = luceneSearchCriteria.Query;
                        }

                        var facetResult = simpleFacetedSearch.Search(facetQuery);

                        facets = facetResult
                                    .HitsPerFacet
                                    .Select(
                                        x => new Facet()
                                        {
                                            Tag = x.Name.ToString(),
                                            Count = Convert.ToInt32(x.HitCount)
                                        }
                                    )
                                    .ToArray();
                    }

                    // setup the getHightlight func if required
                    if (lookQuery.TextQuery != null && lookQuery.TextQuery.GetHighlight && !string.IsNullOrWhiteSpace(lookQuery.TextQuery.SearchText))
                    {
                        var queryParser = new QueryParser(Lucene.Net.Util.Version.LUCENE_29, LookConstants.TextField, LookService.Analyzer);

                        var queryScorer = new QueryScorer(queryParser
                                                            .Parse(lookQuery.TextQuery.SearchText)
                                                            .Rewrite(indexSearcher.GetIndexReader()));

                        var highlighter = new Highlighter(new SimpleHTMLFormatter("<strong>", "</strong>"), queryScorer);

                        // update the getHightlight func
                        getHighlight = (x) =>
                        {
                            var tokenStream = LookService.Analyzer.TokenStream(LookConstants.TextField, new StringReader(x));

                            var highlight = highlighter.GetBestFragments(
                                                            tokenStream,
                                                            x,
                                                            1, // max number of fragments
                                                            "..."); 

                            return new HtmlString(highlight);
                        };
                    }

                    return new LookResult(LookSearchService.GetLookMatches(
                                                                lookQuery,
                                                                indexSearcher,
                                                                topDocs,
                                                                getHighlight,
                                                                getDistance),
                                            topDocs.TotalHits,
                                            facets);
                }
            }            

            return LookResult.Empty;
        }

        /// <summary>
        /// Supplied with the result of a Lucene query, this method will yield a constructed LookMatch for each in order
        /// </summary>
        /// <param name="indexSearcher">The searcher supplied to get the Lucene doc for each id in the Lucene results (topDocs)</param>
        /// <param name="topDocs">The results of the Lucene query (a collection of ids in an order)</param>
        /// <param name="getHighlight">Function used to get the highlight text for a given result text</param>
        /// <param name="getDistance">Function used to calculate distance (if a location was supplied in the original query)</param>
        /// <returns></returns>
        private static IEnumerable<LookMatch> GetLookMatches(
                                                    LookQuery lookQuery,
                                                    IndexSearcher indexSearcher,
                                                    TopDocs topDocs,
                                                    Func<string, IHtmlString> getHighlight,
                                                    Func<int, double?> getDistance)
        {
            // flag to indicate that the query has requested the full text to be returned
            bool getText = lookQuery.TextQuery != null && lookQuery.TextQuery.GetText;

            var fields = new List<string>();

            fields.Add(LuceneIndexer.IndexNodeIdFieldName); // "__NodeId"
            fields.Add(LookConstants.NameField);
            fields.Add(LookConstants.DateField);

            // if a highlight function is supplied (or text requested)
            if (getHighlight != null || getText)  { fields.Add(LookConstants.TextField); }

            fields.Add(LookConstants.TagsField);
            fields.Add(LookConstants.LocationField);

            var mapFieldSelector = new MapFieldSelector(fields.ToArray());

            // if highlight func does not exist, then create one to always return null
            if (getHighlight == null) { getHighlight = x => null; }

            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var docId = scoreDoc.doc;

                var doc = indexSearcher.Doc(docId, mapFieldSelector);

                var lookMatch = new LookMatch(
                    Convert.ToInt32(doc.Get(LuceneIndexer.IndexNodeIdFieldName)),
                    getHighlight(doc.Get(LookConstants.TextField)),
                    getText ? doc.Get(LookConstants.TextField) : null,
                    LookSearchService.GetTags(doc.GetFields(LookConstants.TagsField)),
                    LookSearchService.GetDate(doc.Get(LookConstants.DateField)),
                    doc.Get(LookConstants.NameField),
                    doc.Get(LookConstants.LocationField) != null ? new Location(doc.Get(LookConstants.LocationField)) : null,
                    getDistance(docId),
                    scoreDoc.score
                );

                yield return lookMatch;
            }
        }

        /// <summary>
        /// Helper for when building a look match obj
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        private static string[] GetTags(Field[] fields)
        {
            if (fields != null)
            {
                return fields
                        .Select(y => y.StringValue())
                        .Where(y => !string.IsNullOrWhiteSpace(y))
                        .ToArray();
            }

            return new string[] { };
        }

        /// <summary>
        /// Helper for when building a look match obj
        /// </summary>
        /// <param name="dateValue"></param>
        /// <returns></returns>
        private static DateTime? GetDate(string dateValue)
        {
            DateTime? date = null;

            try
            {
                date = DateTools.StringToDate(dateValue);
            }
            catch
            {
                LogHelper.Info(typeof(LookSearchService), $"Unable to convert string '{dateValue}' into a DateTime");
            }

            return date;
        }
    }
}
