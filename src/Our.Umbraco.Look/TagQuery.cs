﻿using Our.Umbraco.Look.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Our.Umbraco.Look
{
    /// <summary>
    /// 
    /// </summary>
    public class TagQuery
    {
        /// <summary>
        /// Must have all the tags in the collection
        /// </summary>
        public LookTag[] All { get; set; }

        /// <summary>
        /// Must have at least one tag from each collection.
        /// </summary>
        public LookTag[][] Any { get; set; }

        /// <summary>
        /// Must not have any tags in the collection
        /// </summary>
        public LookTag[] None { get; set; }

        /// <summary>
        /// when null, facets are not calculated, but when string[], each string value represents the tag group field to facet on, the empty string or whitespace = empty group
        /// The count value for a returned tag indicates how may results would be expected should that tag be added into the AllTags collection of this query
        /// </summary>
        public TagFacetQuery FacetOn { get; set; }

        /// <summary>
        /// Helper to simplify the construction of LookTag array, by being able to supply a raw collection of tag strings
        /// </summary>
        /// <param name="tags"></param>
        /// <returns></returns>
        public static LookTag[] MakeTags(params string[] tags)
        {
            List<LookTag> lookTags = new List<LookTag>();

            if (tags != null)
            {
                foreach(var tag in tags)
                {
                    lookTags.Add(new LookTag(tag));
                }
            }

            return lookTags.ToArray();
        }

        /// <summary>
        /// Helper to simplify the construction of LookTag array
        /// </summary>
        /// <param name="tags"></param>
        /// <returns></returns>
        public static LookTag[] MakeTags(IEnumerable<string> tags)
        {
            return TagQuery.MakeTags(tags.ToArray());
        }

        public override bool Equals(object obj)
        {
            var tagQuery = obj as TagQuery;

            var anyQueryEqual = new Func<LookTag[][], LookTag[][], bool>((first, second) =>
            {
                if (first == null && second == null) return true;

                if (first == null || second == null) return false;

                if (first.Count() != second.Count()) return false;

                var firstStack = new Stack<LookTag[]>(first);

                var areEqual = true;

                do
                {
                    var firstCollection = firstStack.Pop();

                    var matchingCollectionFound = false;

                    var secondStack = new Stack<LookTag[]>(second);

                    do
                    {
                        var secondCollection = secondStack.Pop();

                        matchingCollectionFound = firstCollection.BothNullOrElementsEqual(secondCollection);
                    }
                    while (!matchingCollectionFound && secondStack.Any());

                    areEqual = matchingCollectionFound;
                }
                while (areEqual && firstStack.Any());
                
                return areEqual;
            });

            return tagQuery != null
                    && tagQuery.All.BothNullOrElementsEqual(this.All)                    
                    && tagQuery.None.BothNullOrElementsEqual(this.None)
                    && tagQuery.FacetOn.BothNullOrEquals(this.FacetOn)
                    && anyQueryEqual(tagQuery.Any, this.Any); // potentially the slowest of all clauses, so last
        }

        internal TagQuery Clone()
        {
            return (TagQuery)this.MemberwiseClone();
        }
    }
}