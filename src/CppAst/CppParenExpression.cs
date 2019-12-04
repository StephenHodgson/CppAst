using System.Text;

namespace CppAst
{
    /// <summary>
    /// An expression surrounding another expression by parenthesis.
    /// </summary>
    public class CppParenExpression : CppExpression
    {
        public CppParenExpression() : base(CppExpressionKind.Paren)
        {
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append("(");
            ArgumentsSeparatedByCommaToString(builder);
            builder.Append(")");
            return builder.ToString();
        }
    }
}