namespace CppAst
{
    /// <summary>
    /// A simple text comment entry.
    /// </summary>
    public class CppCommentText : CppCommentTextBase
    {
        public CppCommentText() : base(CppCommentKind.Text)
        {
        }
    }
}