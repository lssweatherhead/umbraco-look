﻿using Examine;
using Examine.Providers;
using Our.Umbraco.Look.BackOffice.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace Our.Umbraco.Look.BackOffice.Models
{
    internal class RootTreeNode : BaseTreeNode
    {
        public override string Name => string.Empty; //null; //"Look";

        public override string Icon => string.Empty; //null; //"icon-zoom-in";

        /// <summary>
        /// For each examine searcher (Examine & Look) create a child node
        /// </summary>
        public override ILookTreeNode[] Children
        {
            get
            {
                var children = new List<ILookTreeNode>();

                var searchProviders = ExamineManager.Instance.SearchProviderCollection;

                foreach (var searchProvider in searchProviders)
                {
                    var baseSearchProvider = searchProvider as BaseSearchProvider;

                    if (baseSearchProvider != null) // safety check
                    {
                        children.Add(new SearcherTreeNode(baseSearchProvider));
                    }
                }

                return children.OrderBy(x => x.Name).ToArray();
            }
        }

        internal RootTreeNode(string id) : base(id) { }
    }
}