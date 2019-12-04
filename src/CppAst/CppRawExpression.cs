using System.Collections.Generic;

namespace CppAst
{
    /// <summary>
    /// An expression that is not exposed in details but only through a list of <see cref="CppToken"/> and a textual representation
    /// </summary>
    public class CppRawExpression : CppExpression
    {
        public CppRawExpression(CppExpressionKind kind) : base(kind)
        {
            Tokens = new List<CppToken>();
        }

        /// <summary>
        /// Gets the tokens associated to this raw expression.
        /// </summary>
        public List<CppToken> Tokens { get; }

        /// <summary>
        /// Gets or sets a textual representation from the tokens. 
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Update the <see cref="Text"/> representation from the <see cref="Tokens"/>.
        /// </summary>
        public void UpdateTextFromTokens()
        {
            Text = CppToken.TokensToString(Tokens);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Text;
        }
    }
}