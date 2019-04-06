﻿using Examine.LuceneEngine.Providers;
using Lucene.Net.Search;
using Our.Umbraco.Look.Exceptions;
using Our.Umbraco.Look.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Our.Umbraco.Look.Services
{
    internal partial class LookService
    {
        /// <summary>
        /// Perform a Look search
        /// </summary>
        /// <param name="lookQuery">A LookQuery model for the search criteria</param>
        /// <returns>A LookResult model for the search response</returns>
        public static LookResult Search(LookQuery lookQuery)
        {
            // flag to indicate whether there are any query clauses in the supplied LookQuery
            bool hasQuery = lookQuery?.Compiled != null ? true : false;

            if (lookQuery == null)
            {
                return LookResult.Error("LookQuery object was null");
            }

            if (lookQuery.SearchingContext == null) // supplied by unit test to skip examine dependency
            {
                // attempt to get searching context from examine searcher name
                lookQuery.SearchingContext = LookService.GetSearchingContext(lookQuery.SearcherName);

                if (lookQuery.SearchingContext == null)
                {
                    return LookResult.Error("SearchingContext was null");
                }
            }

            if (lookQuery.Compiled == null)
            {
                var parsingContext = new ParsingContext();

                try
                {
                    LookService.ParseRawQuery(lookQuery, parsingContext);

                    LookService.ParseExamineQuery(lookQuery, parsingContext);

                    LookService.ParseNodeQuery(lookQuery, parsingContext);

                    LookService.ParseNameQuery(lookQuery, parsingContext);

                    LookService.ParseDateQuery(lookQuery, parsingContext);

                    LookService.ParseTextQuery(lookQuery, parsingContext);

                    LookService.ParseTagQuery(lookQuery, parsingContext);

                    LookService.ParseLocationQuery(lookQuery, parsingContext);
                }
                catch (ParsingException parsingException)
                {
                    return LookResult.Error(parsingException.Message);
                }

                if (parsingContext.HasQuery)
                {
                    hasQuery = true;

                    switch (lookQuery.SortOn)
                    {
                        case SortOn.Name: // a -> z
                            parsingContext.Sort = new Sort(new SortField(LuceneIndexer.SortedFieldNamePrefix + LookConstants.NameField, SortField.STRING));
                            break;

                        case SortOn.DateAscending: // oldest -> newest
                            parsingContext.Sort = new Sort(new SortField(LuceneIndexer.SortedFieldNamePrefix + LookConstants.DateField, SortField.LONG, false));
                            break;

                        case SortOn.DateDescending: // newest -> oldest
                            parsingContext.Sort = new Sort(new SortField(LuceneIndexer.SortedFieldNamePrefix + LookConstants.DateField, SortField.LONG, true));
                            break;

                        // SortOn.Distance already set (if valid)
                    }

                    lookQuery.Compiled = new LookQueryCompiled(
                                                        lookQuery,
                                                        parsingContext.Query,
                                                        parsingContext.Filter,
                                                        parsingContext.Sort ?? new Sort(SortField.FIELD_SCORE),
                                                        parsingContext.GetHighlight,
                                                        parsingContext.GetDistance);
                }
            }

            if (!hasQuery)
            {
                return LookResult.Error("No query clauses supplied"); // empty failure
            }

            TopDocs topDocs = lookQuery
                                .SearchingContext
                                .IndexSearcher
                                .Search(
                                    lookQuery.Compiled.Query,
                                    lookQuery.Compiled.Filter,
                                    LookService._maxLuceneResults,
                                    lookQuery.Compiled.Sort);

            if (topDocs.TotalHits > 0)
            {
                List<Facet> facets = null;

                if (lookQuery.TagQuery != null && lookQuery.TagQuery.FacetOn != null)
                {
                    facets = new List<Facet>();

                    Query facetQuery = lookQuery.Compiled.Filter != null 
                                            ? (Query)new FilteredQuery(lookQuery.Compiled.Query, lookQuery.Compiled.Filter) 
                                            : lookQuery.Compiled.Query;

                    // do a facet query for each group in the array
                    foreach (var group in lookQuery.TagQuery.FacetOn.TagGroups)
                    {
                        var simpleFacetedSearch = new SimpleFacetedSearch(
                                                        lookQuery.SearchingContext.IndexSearcher.GetIndexReader(),
                                                        LookConstants.TagsField + group);

                        var facetResult = simpleFacetedSearch.Search(facetQuery);

                        facets.AddRange(
                                facetResult
                                    .HitsPerFacet
                                    .Select(
                                        x => new Facet()
                                        {
                                            Tags = new LookTag[] { new LookTag(group, x.Name.ToString()) },
                                            Count = Convert.ToInt32(x.HitCount)
                                        }
                                    ));
                    }
                }

                return new LookResult(
                                lookQuery,
                                topDocs,
                                facets != null ? facets.ToArray() : new Facet[] { });
            }

            return LookResult.Empty(); // empty success
        }
    }
}
