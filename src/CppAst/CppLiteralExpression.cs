namespace CppAst
{
    /// <summary>
    /// A literal expression.
    /// </summary>
    public class CppLiteralExpression : CppExpression
    {
        public CppLiteralExpression(CppExpressionKind kind, string value) : base(kind)
        {
            Value = value;
        }

        /// <summary>
        /// A textual representation of the literal value.
        /// </summary>
        public string Value { get; set; }

        public override string ToString()
        {
            return Value;
        }
    }
}