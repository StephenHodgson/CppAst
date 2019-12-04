// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace CppAst
{
    /// <summary>
    /// Base class for expressions used in <see cref="CppField.InitExpression"/> and <see cref="CppParameter.InitExpression"/>
    /// </summary>
    public abstract class CppExpression : CppElement
    {
        protected CppExpression(CppExpressionKind kind)
        {
            Kind = kind;
        }

        /// <summary>
        /// Gets the kind of this expression.
        /// </summary>
        public CppExpressionKind Kind { get; }

        /// <summary>
        /// Gets the arguments of this expression. Might be null.
        /// </summary>
        public List<CppExpression> Arguments { get; set; }

        /// <summary>
        /// Adds an argument to this expression.
        /// </summary>
        /// <param name="arg">An argument</param>
        public void AddArgument(CppExpression arg)
        {
            if (arg == null) throw new ArgumentNullException(nameof(arg));
            if (Arguments == null) Arguments = new List<CppExpression>();
            Arguments.Add(arg);
        }

        protected void ArgumentsSeparatedByCommaToString(StringBuilder builder)
        {
            if (Arguments != null)
            {
                for (var i = 0; i < Arguments.Count; i++)
                {
                    var expression = Arguments[i];
                    if (i > 0) builder.Append(", ");
                    builder.Append(expression);
                }
            }
        }
    }
}