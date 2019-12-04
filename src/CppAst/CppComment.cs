// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Collections.Generic;
using System.Text;

namespace CppAst
{
    /// <summary>
    /// Base class for all comments.
    /// </summary>
    public abstract class CppComment
    {
        protected CppComment(CppCommentKind kind)
        {
            Kind = kind;
        }

        /// <summary>
        /// The kind of comments.
        /// </summary>
        public CppCommentKind Kind { get; }

        /// <summary>
        /// Gets a list of children. Might be null.
        /// </summary>
        public List<CppComment> Children { get; set; }

        protected internal abstract void ToString(StringBuilder builder);

        protected void ChildrenToString(StringBuilder builder)
        {
            if (Children != null)
            {
                foreach (var children in Children)
                {
                    children.ToString(builder);
                }
            }
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            ToString(builder);
            return builder.ToString();
        }

        public string ChildrenToString()
        {
            var builder = new StringBuilder();
            ChildrenToString(builder);
            return builder.ToString();
        }
    }
}