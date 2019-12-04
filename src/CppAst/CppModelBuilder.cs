// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ClangSharp;

namespace CppAst
{
    /// <summary>
    /// Internal class used to build the entire C++ model from the libclang representation.
    /// </summary>
    internal class CppModelBuilder
    {
        private readonly CppCompilation _rootCompilation;
        private readonly CppContainerContext _rootContainerContext;
        private readonly Dictionary<string, CppContainerContext> _containers;
        private readonly Dictionary<string, CppType> _typedefs;
        private bool _isEntryVisitSystem;

        public CppModelBuilder()
        {
            _containers = new Dictionary<string, CppContainerContext>();
            _rootCompilation = new CppCompilation();
            _typedefs = new Dictionary<string, CppType>();
            _rootContainerContext = new CppContainerContext(_rootCompilation);
        }

        public bool AutoSquashTypedef { get; set; }

        public CppCompilation RootCompilation => _rootCompilation;

        public CXChildVisitResult VisitTranslationUnit(CXCursor cursor, CXCursor parent, CXClientData data)
        {
            Debug.Assert(parent.Kind == CXCursorKind.CXCursor_TranslationUnit || parent.Kind == CXCursorKind.CXCursor_UnexposedDecl);

            _rootContainerContext.Container = _rootCompilation;
            _isEntryVisitSystem = cursor.Location.IsInSystemHeader;

            if (cursor.Location.IsInSystemHeader)
            {
                _rootContainerContext.Container = _rootCompilation.System;
            }

            return VisitMember(cursor, parent, data);
        }

        private CppContainerContext GetOrCreateDeclarationContainer(CXCursor cursor, CXClientData data)
        {
            var fullName = cursor.UnifiedSymbolResolution.CString;

            if (_containers.TryGetValue(fullName, out var containerContext))
            {
                return containerContext;
            }

            ICppContainer parent = null;
            ICppContainer symbol = null;

            if (cursor.Kind != CXCursorKind.CXCursor_TranslationUnit && cursor.Kind != CXCursorKind.CXCursor_UnexposedDecl)
            {
                parent = GetOrCreateDeclarationContainer(cursor.SemanticParent, data).Container;
            }

            var parentDeclarationContainer = (ICppDeclarationContainer)parent;
            var parentGlobalDeclarationContainer = parent as ICppGlobalDeclarationContainer;
            var defaultContainerVisibility = CppVisibility.Default;

            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_Namespace:
                    Debug.Assert(parentGlobalDeclarationContainer != null);
                    var ns = new CppNamespace(GetCursorSpelling(cursor));
                    symbol = ns;
                    defaultContainerVisibility = CppVisibility.Default;
                    parentGlobalDeclarationContainer.Namespaces.Add(ns);
                    break;

                case CXCursorKind.CXCursor_EnumDecl:
                    Debug.Assert(parent != null);
                    var cppEnum = new CppEnum(GetCursorSpelling(cursor))
                    {
                        IsAnonymous = cursor.IsAnonymous
                    };
                    parentDeclarationContainer.Enums.Add(cppEnum);
                    symbol = cppEnum;
                    break;

                case CXCursorKind.CXCursor_ClassTemplate:
                case CXCursorKind.CXCursor_ClassDecl:
                case CXCursorKind.CXCursor_StructDecl:
                case CXCursorKind.CXCursor_UnionDecl:
                    Debug.Assert(parent != null);
                    var cppClass = new CppClass(GetCursorSpelling(cursor));
                    parentDeclarationContainer.Classes.Add(cppClass);
                    symbol = cppClass;
                    cppClass.IsAnonymous = cursor.IsAnonymous;
                    switch (cursor.Kind)
                    {
                        case CXCursorKind.CXCursor_ClassDecl:
                            cppClass.ClassKind = CppClassKind.Class;
                            break;
                        case CXCursorKind.CXCursor_StructDecl:
                            cppClass.ClassKind = CppClassKind.Struct;
                            break;
                        case CXCursorKind.CXCursor_UnionDecl:
                            cppClass.ClassKind = CppClassKind.Union;
                            break;
                    }

                    if (cursor.Kind == CXCursorKind.CXCursor_ClassTemplate)
                    {
                        cursor.VisitChildren((childCursor, classCursor, clientData) =>
                        {
                            switch (childCursor.Kind)
                            {
                                case CXCursorKind.CXCursor_TemplateTypeParameter:
                                    var parameterTypeName = new CppTemplateParameterType(GetCursorSpelling(childCursor));
                                    cppClass.TemplateParameters.Add(parameterTypeName);
                                    break;
                            }

                            return CXChildVisitResult.CXChildVisit_Continue;
                        }, data);
                    }

                    defaultContainerVisibility = cursor.Kind == CXCursorKind.CXCursor_ClassDecl ? CppVisibility.Private : CppVisibility.Public;
                    break;
                case CXCursorKind.CXCursor_TranslationUnit:
                case CXCursorKind.CXCursor_UnexposedDecl:
                    return _rootContainerContext;
                default:
                    Unhandled(cursor);
                    break;
            }

            containerContext = new CppContainerContext(symbol) { CurrentVisibility = defaultContainerVisibility };

            _containers.Add(fullName, containerContext);
            return containerContext;
        }

        private TCppElement GetOrCreateDeclarationContainer<TCppElement>(CXCursor cursor, CXClientData data, out CppContainerContext context) where TCppElement : CppElement, ICppContainer
        {
            context = GetOrCreateDeclarationContainer(cursor, data);
            if (context.Container is TCppElement typedCppElement)
            {
                return typedCppElement;
            }
            throw new InvalidOperationException($"The element `{context.Container}` doesn't match the expected type `{typeof(TCppElement)}");
        }

        private CppNamespace VisitNamespace(CXCursor cursor, CXClientData data)
        {
            // Create the container if not already created
            var ns = GetOrCreateDeclarationContainer<CppNamespace>(cursor, data, out _);
            cursor.VisitChildren(VisitMember, data);
            return ns;
        }

        private CppClass VisitClassDecl(CXCursor cursor, CXClientData data)
        {
            var cppStruct = GetOrCreateDeclarationContainer<CppClass>(cursor, data, out var context);
            if (cursor.IsDefinition && !cppStruct.IsDefinition)
            {
                cppStruct.Attributes = ParseAttributes(cursor);
                cppStruct.IsDefinition = true;
                cppStruct.SizeOf = (int)cursor.Type.SizeOf;
                context.IsChildrenVisited = true;
                cursor.VisitChildren(VisitMember, data);
            }
            return cppStruct;
        }

        private CXChildVisitResult VisitMember(CXCursor cursor, CXCursor parent, CXClientData data)
        {
            CppElement element = null;

            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_FieldDecl:
                case CXCursorKind.CXCursor_VarDecl:
                    {
                        var containerContext = GetOrCreateDeclarationContainer(parent, data);
                        element = VisitFieldOrVariable(containerContext, cursor, data);
                        break;
                    }
                case CXCursorKind.CXCursor_EnumConstantDecl:
                    {
                        var containerContext = GetOrCreateDeclarationContainer(parent, data);
                        var cppEnum = (CppEnum)containerContext.Container;
                        var enumItem = new CppEnumItem(GetCursorSpelling(cursor), cursor.EnumConstantDeclValue);

                        VisitInitValue(cursor, data, out var enumItemExpression, out _);
                        enumItem.ValueExpression = enumItemExpression;

                        cppEnum.Items.Add(enumItem);
                        element = enumItem;
                        break;
                    }
                case CXCursorKind.CXCursor_Namespace:
                    element = VisitNamespace(cursor, data);
                    break;

                case CXCursorKind.CXCursor_ClassTemplate:
                case CXCursorKind.CXCursor_ClassDecl:
                case CXCursorKind.CXCursor_StructDecl:
                case CXCursorKind.CXCursor_UnionDecl:
                    element = VisitClassDecl(cursor, data);
                    break;

                case CXCursorKind.CXCursor_EnumDecl:
                    element = VisitEnumDecl(cursor, data);
                    break;

                case CXCursorKind.CXCursor_TypedefDecl:
                    element = VisitTypeDefDecl(cursor, data);
                    break;

                case CXCursorKind.CXCursor_FunctionTemplate:
                case CXCursorKind.CXCursor_FunctionDecl:
                case CXCursorKind.CXCursor_Constructor:
                case CXCursorKind.CXCursor_CXXMethod:
                    element = VisitFunctionDecl(cursor, parent, data);
                    break;

                case CXCursorKind.CXCursor_UsingDirective:
                    // We don't visit directive
                    break;
                case CXCursorKind.CXCursor_UnexposedDecl:
                    return CXChildVisitResult.CXChildVisit_Recurse;

                case CXCursorKind.CXCursor_CXXBaseSpecifier:
                    {
                        var cppClass = (CppClass)GetOrCreateDeclarationContainer(parent, data).Container;
                        var baseType = GetCppType(cursor.Type.Declaration, cursor.Type, cursor, data);
                        var cppBaseType = new CppBaseType(baseType)
                        {
                            Visibility = GetVisibility(cursor.CXXAccessSpecifier),
                            IsVirtual = cursor.IsVirtualBase
                        };
                        cppClass.BaseTypes.Add(cppBaseType);
                        break;
                    }

                case CXCursorKind.CXCursor_CXXAccessSpecifier:
                    {
                        var containerContext = GetOrCreateDeclarationContainer(parent, data);
                        containerContext.CurrentVisibility = GetVisibility(cursor.CXXAccessSpecifier);
                    }

                    break;

                case CXCursorKind.CXCursor_MacroDefinition:
                    element = ParseMacro(cursor);
                    break;
                case CXCursorKind.CXCursor_MacroExpansion:
                    break;

                default:
                    WarningUnhandled(cursor, parent);
                    break;
            }

            if (element != null)
            {
                AssignSourceSpan(cursor, element);
            }

            if (element is ICppDeclaration cppDeclaration)
            {
                cppDeclaration.Comment = GetComment(cursor);
            }

            return CXChildVisitResult.CXChildVisit_Continue;
        }

        private CppComment GetComment(CXCursor cursor)
        {
            return GetComment(cursor.ParsedComment);
        }

        private CppComment GetComment(CXComment cxComment)
        {
            CppComment cppComment;
            var removeTrailingEmptyText = false;
            var cppKind = GetCommentKind(cxComment.Kind);

            switch (cppKind)
            {
                case CppCommentKind.Null:
                    return null;

                case CppCommentKind.Text:
                    cppComment = new CppCommentText
                    {
                        Text = cxComment.TextComment_Text.ToString().TrimStart()
                    };
                    break;

                case CppCommentKind.InlineCommand:
                    var inline = new CppCommentInlineCommand
                    {
                        CommandName = cxComment.InlineCommandComment_CommandName.ToString()
                    };

                    cppComment = inline;

                    switch (cxComment.InlineCommandComment_RenderKind)
                    {
                        case CXCommentInlineCommandRenderKind.CXCommentInlineCommandRenderKind_Normal:
                            inline.RenderKind = CppCommentInlineCommandRenderKind.Normal;
                            break;
                        case CXCommentInlineCommandRenderKind.CXCommentInlineCommandRenderKind_Bold:
                            inline.RenderKind = CppCommentInlineCommandRenderKind.Bold;
                            break;
                        case CXCommentInlineCommandRenderKind.CXCommentInlineCommandRenderKind_Monospaced:
                            inline.RenderKind = CppCommentInlineCommandRenderKind.Monospaced;
                            break;
                        case CXCommentInlineCommandRenderKind.CXCommentInlineCommandRenderKind_Emphasized:
                            inline.RenderKind = CppCommentInlineCommandRenderKind.Emphasized;
                            break;
                    }

                    for (uint i = 0; i < cxComment.InlineCommandComment_NumArgs; i++)
                    {
                        inline.Arguments.Add(cxComment.InlineCommandComment_GetArgText(i).ToString());
                    }

                    break;

                case CppCommentKind.HtmlStartTag:
                    var htmlStartTag = new CppCommentHtmlStartTag
                    {
                        TagName = cxComment.HtmlTagComment_TagName.ToString(),
                        IsSelfClosing = cxComment.HtmlStartTagComment_IsSelfClosing
                    };

                    for (uint i = 0; i < cxComment.HtmlStartTag_NumAttrs; i++)
                    {
                        htmlStartTag.Attributes.Add(new KeyValuePair<string, string>(
                            cxComment.HtmlStartTag_GetAttrName(i).ToString(),
                            cxComment.HtmlStartTag_GetAttrValue(i).ToString()
                            ));
                    }

                    cppComment = htmlStartTag;
                    break;

                case CppCommentKind.HtmlEndTag:
                    var htmlEndTag = new CppCommentHtmlEndTag();
                    htmlEndTag.TagName = cxComment.HtmlTagComment_TagName.ToString();
                    cppComment = htmlEndTag;
                    break;

                case CppCommentKind.Paragraph:
                    cppComment = new CppCommentParagraph();
                    break;

                case CppCommentKind.BlockCommand:
                    var blockComment = new CppCommentBlockCommand();
                    blockComment.CommandName = cxComment.BlockCommandComment_CommandName.ToString();

                    for (uint i = 0; i < cxComment.BlockCommandComment_NumArgs; i++)
                    {
                        blockComment.Arguments.Add(cxComment.BlockCommandComment_GetArgText(i).ToString());
                    }

                    removeTrailingEmptyText = true;
                    cppComment = blockComment;
                    break;

                case CppCommentKind.ParamCommand:
                    var paramComment = new CppCommentParamCommand();
                    paramComment.CommandName = "param";
                    paramComment.ParamName = cxComment.ParamCommandComment_ParamName.ToString();
                    paramComment.IsDirectionExplicit = cxComment.ParamCommandComment_IsDirectionExplicit;
                    paramComment.IsParamIndexValid = cxComment.ParamCommandComment_IsParamIndexValid;
                    paramComment.ParamIndex = (int)cxComment.ParamCommandComment_ParamIndex;
                    switch (cxComment.ParamCommandComment_Direction)
                    {
                        case CXCommentParamPassDirection.CXCommentParamPassDirection_In:
                            paramComment.Direction = CppCommentParamDirection.In;
                            break;
                        case CXCommentParamPassDirection.CXCommentParamPassDirection_Out:
                            paramComment.Direction = CppCommentParamDirection.Out;
                            break;
                        case CXCommentParamPassDirection.CXCommentParamPassDirection_InOut:
                            paramComment.Direction = CppCommentParamDirection.InOut;
                            break;
                    }

                    removeTrailingEmptyText = true;
                    cppComment = paramComment;
                    break;

                case CppCommentKind.TemplateParamCommand:
                    var tParamComment = new CppCommentTemplateParamCommand();
                    tParamComment.CommandName = "tparam";
                    tParamComment.ParamName = cxComment.TParamCommandComment_ParamName.ToString();
                    tParamComment.Depth = (int)cxComment.TParamCommandComment_Depth;
                    // TODO: index
                    tParamComment.IsPositionValid = cxComment.TParamCommandComment_IsParamPositionValid;

                    removeTrailingEmptyText = true;
                    cppComment = tParamComment;
                    break;
                case CppCommentKind.VerbatimBlockCommand:
                    var verbatimBlock = new CppCommentVerbatimBlockCommand();
                    verbatimBlock.CommandName = cxComment.BlockCommandComment_CommandName.ToString();
                    for (uint i = 0; i < cxComment.BlockCommandComment_NumArgs; i++)
                    {
                        verbatimBlock.Arguments.Add(cxComment.BlockCommandComment_GetArgText(i).ToString());
                    }
                    cppComment = verbatimBlock;
                    break;
                case CppCommentKind.VerbatimBlockLine:
                    cppComment = new CppCommentVerbatimBlockLine
                    {
                        Text = cxComment.VerbatimBlockLineComment_Text.ToString()
                    };
                    break;
                case CppCommentKind.VerbatimLine:
                    cppComment = new CppCommentVerbatimLine
                    {
                        Text = cxComment.VerbatimLineComment_Text.ToString()
                    };
                    break;
                case CppCommentKind.Full:
                    cppComment = new CppCommentFull();
                    break;
                default:
                    return null;
            }

            Debug.Assert(cppComment != null);

            for (uint i = 0; i < cxComment.NumChildren; i++)
            {
                var cxChildComment = cxComment.GetChild(i);
                var cppChildComment = GetComment(cxChildComment);

                if (cppChildComment != null)
                {
                    if (cppComment.Children == null)
                    {
                        cppComment.Children = new List<CppComment>();
                    }

                    cppComment.Children.Add(cppChildComment);
                }
            }

            if (removeTrailingEmptyText)
            {
                RemoveTrailingEmptyText(cppComment);
            }

            return cppComment;
        }

        private static void RemoveTrailingEmptyText(CppComment cppComment)
        {
            // Remove the last paragraph if it is an empty string text
            if (cppComment.Children != null && cppComment.Children.Count > 0 && cppComment.Children[cppComment.Children.Count - 1] is CppCommentParagraph paragraph)
            {
                // Remove the last paragraph if it is an empty string text
                if (paragraph.Children != null && paragraph.Children.Count > 0 && paragraph.Children[paragraph.Children.Count - 1] is CppCommentText text && string.IsNullOrWhiteSpace(text.Text))
                {
                    paragraph.Children.RemoveAt(paragraph.Children.Count - 1);
                }
            }
        }

        private CppCommentKind GetCommentKind(CXCommentKind kind)
        {
            switch (kind)
            {
                case CXCommentKind.CXComment_Null:
                    return CppCommentKind.Null;
                case CXCommentKind.CXComment_Text:
                    return CppCommentKind.Text;
                case CXCommentKind.CXComment_InlineCommand:
                    return CppCommentKind.InlineCommand;
                case CXCommentKind.CXComment_HTMLStartTag:
                    return CppCommentKind.HtmlStartTag;
                case CXCommentKind.CXComment_HTMLEndTag:
                    return CppCommentKind.HtmlEndTag;
                case CXCommentKind.CXComment_Paragraph:
                    return CppCommentKind.Paragraph;
                case CXCommentKind.CXComment_BlockCommand:
                    return CppCommentKind.BlockCommand;
                case CXCommentKind.CXComment_ParamCommand:
                    return CppCommentKind.ParamCommand;
                case CXCommentKind.CXComment_TParamCommand:
                    return CppCommentKind.TemplateParamCommand;
                case CXCommentKind.CXComment_VerbatimBlockCommand:
                    return CppCommentKind.VerbatimBlockCommand;
                case CXCommentKind.CXComment_VerbatimBlockLine:
                    return CppCommentKind.VerbatimBlockLine;
                case CXCommentKind.CXComment_VerbatimLine:
                    return CppCommentKind.VerbatimLine;
                case CXCommentKind.CXComment_FullComment:
                    return CppCommentKind.Full;
                default:
                    throw new ArgumentOutOfRangeException($"Unsupported comment kind `{kind}`");
            }
        }

        private CppMacro ParseMacro(CXCursor cursor)
        {
            // TODO: reuse internal class Tokenizer

            // As we don't have an API to check macros, we are 
            var originalRange = cursor.Extent;
            var tu = cursor.TranslationUnit;

            // Try to extend the parsing of the macro to the end of line in order to recover comments
            originalRange.End.GetFileLocation(out var startFile, out var endLine, out _, out _);
            var range = originalRange;

            if (startFile.Pointer != IntPtr.Zero)
            {
                var nextLineLocation = clang.getLocation(tu, startFile, endLine + 1, 1);

                if (!nextLineLocation.Equals(CXSourceLocation.Null))
                {
                    range = clang.getRange(originalRange.Start, nextLineLocation);
                }
            }

            tu.Tokenize(range, out var tokens);

            var name = GetCursorSpelling(cursor);
            var cppMacro = new CppMacro(name);

            uint previousLine = 0;
            uint previousColumn = 0;
            bool parsingMacroParameters = false;
            List<string> macroParameters = null;

            // Loop decoding tokens for the value
            // We need to parse 
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                var tokenRange = token.GetExtent(tu);
                tokenRange.Start.GetFileLocation(out _, out var line, out var column, out _);

                if (line >= endLine + 1)
                {
                    break;
                }
                var tokenStr = token.GetSpelling(tu).CString;

                // If we are parsing the token right after the MACRO name token
                // if the `(` is right after the name without 
                if (i == 1 && tokenStr == "(" && (previousLine == line && previousColumn == column))
                {
                    parsingMacroParameters = true;
                    macroParameters = new List<string>();
                }

                tokenRange.End.GetFileLocation(out _, out previousLine, out previousColumn, out _);

                if (parsingMacroParameters)
                {
                    if (tokenStr == ")")
                    {
                        parsingMacroParameters = false;
                    }
                    else if (token.Kind != CXTokenKind.CXToken_Punctuation)
                    {
                        macroParameters.Add(tokenStr);
                    }
                }
                else if (i > 0)
                {
                    CppTokenKind cppTokenKind = 0;
                    switch (token.Kind)
                    {
                        case CXTokenKind.CXToken_Punctuation:
                            cppTokenKind = CppTokenKind.Punctuation;
                            break;
                        case CXTokenKind.CXToken_Keyword:
                            cppTokenKind = CppTokenKind.Keyword;
                            break;
                        case CXTokenKind.CXToken_Identifier:
                            cppTokenKind = CppTokenKind.Identifier;
                            break;
                        case CXTokenKind.CXToken_Literal:
                            cppTokenKind = CppTokenKind.Literal;
                            break;
                        case CXTokenKind.CXToken_Comment:
                            cppTokenKind = CppTokenKind.Comment;
                            break;
                        default:
                            _rootCompilation.Diagnostics.Warning($"Token kind {tokenStr} is not supported for macros", GetSourceLocation(token.GetLocation(tu)));
                            break;
                    }

                    var cppToken = new CppToken(cppTokenKind, tokenStr)
                    {
                        Span = new CppSourceSpan(GetSourceLocation(tokenRange.Start), GetSourceLocation(tokenRange.End))
                    };

                    cppMacro.Tokens.Add(cppToken);
                }
            }

            // Update the value from the tokens
            cppMacro.UpdateValueFromTokens();
            cppMacro.Parameters = macroParameters;

            var globalContainer = (CppGlobalDeclarationContainer)_rootContainerContext.DeclarationContainer;
            globalContainer.Macros.Add(cppMacro);

            return cppMacro;
        }

        private static CppVisibility GetVisibility(CX_CXXAccessSpecifier accessSpecifier)
        {
            switch (accessSpecifier)
            {
                case CX_CXXAccessSpecifier.CX_CXXProtected:
                    return CppVisibility.Protected;
                case CX_CXXAccessSpecifier.CX_CXXPrivate:
                    return CppVisibility.Private;
                default:
                    return CppVisibility.Public;
            }
        }

        private static void AssignSourceSpan(CXCursor cursor, CppElement element)
        {
            var start = cursor.Extent.Start;
            var end = cursor.Extent.End;
            element.Span = new CppSourceSpan(GetSourceLocation(start), GetSourceLocation(end));
        }

        public static CppSourceLocation GetSourceLocation(CXSourceLocation start)
        {
            start.GetFileLocation(out var file, out var line, out var column, out var offset);
            return new CppSourceLocation(file.Name.CString, (int)offset, (int)line, (int)column);
        }

        private CppField VisitFieldOrVariable(CppContainerContext containerContext, CXCursor cursor, CXClientData data)
        {
            var fieldName = GetCursorSpelling(cursor);
            var type = GetCppType(cursor.Type.Declaration, cursor.Type, cursor, data);

            var cppField = new CppField(type, fieldName)
            {
                Visibility = containerContext.CurrentVisibility,
                StorageQualifier = GetStorageQualifier(cursor),
                IsBitField = cursor.IsBitField,
                BitFieldWidth = cursor.FieldDeclBitWidth
            };
            containerContext.DeclarationContainer.Fields.Add(cppField);
            cppField.Attributes = ParseAttributes(cursor);

            if (cursor.Kind == CXCursorKind.CXCursor_VarDecl)
            {
                VisitInitValue(cursor, data, out var fieldExpr, out var fieldValue);
                cppField.InitValue = fieldValue;
                cppField.InitExpression = fieldExpr;
            }

            return cppField;
        }

        private void AddAnonymousTypeWithField(CppContainerContext containerContext, CXCursor cursor, CppType fieldType)
        {
            var fieldName = $"__anonymous__{containerContext.DeclarationContainer.Fields.Count}";
            var cppField = new CppField(fieldType, fieldName)
            {
                Visibility = containerContext.CurrentVisibility,
                StorageQualifier = GetStorageQualifier(cursor),
                IsAnonymous = true
            };
            containerContext.DeclarationContainer.Fields.Add(cppField);
            cppField.Attributes = ParseAttributes(cursor);
        }

        private void VisitInitValue(CXCursor cursor, CXClientData data, out CppExpression expression, out CppValue value)
        {
            CppValue localValue = null;
            CppExpression localExpression = null;

            CXChildVisitResult Visit(CXCursor initCursor, CXCursor varCursor, CXClientData clientData)
            {
                if (!IsExpression(initCursor)) { return CXChildVisitResult.CXChildVisit_Continue; }

                localExpression = VisitExpression(initCursor, clientData);
                return CXChildVisitResult.CXChildVisit_Break;
            }

            cursor.VisitChildren(Visit, data);

            // Still tries to extract the compiled value
            var resultEval = clang.Cursor_Evaluate(cursor);

            switch (resultEval.Kind)
            {
                case CXEvalResultKind.CXEval_Int:
                    localValue = new CppValue(resultEval.AsLongLong);
                    break;
                case CXEvalResultKind.CXEval_Float:
                    localValue = new CppValue(resultEval.AsDouble);
                    break;
                case CXEvalResultKind.CXEval_ObjCStrLiteral:
                case CXEvalResultKind.CXEval_StrLiteral:
                case CXEvalResultKind.CXEval_CFStr:
                    localValue = new CppValue(resultEval.AsStr);
                    break;
                case CXEvalResultKind.CXEval_UnExposed:
                    break;
                default:
                    _rootCompilation.Diagnostics.Warning($"Not supported field default value {cursor}", GetSourceLocation(cursor.Location));
                    break;
            }

            expression = localExpression;
            value = localValue;
        }

        private static bool IsExpression(CXCursor cursor)
        {
            return cursor.Kind >= CXCursorKind.CXCursor_FirstExpr && cursor.Kind <= CXCursorKind.CXCursor_LastExpr;
        }

        private CppExpression VisitExpression(CXCursor cursor, CXClientData data)
        {
            CppExpression expression;
            bool visitChildren = false;

            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_UnexposedExpr:
                    expression = new CppRawExpression(CppExpressionKind.Unexposed);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_DeclRefExpr:
                    expression = new CppRawExpression(CppExpressionKind.DeclRef);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_MemberRefExpr:
                    expression = new CppRawExpression(CppExpressionKind.MemberRef);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CallExpr:
                    expression = new CppRawExpression(CppExpressionKind.Call);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_ObjCMessageExpr:
                    expression = new CppRawExpression(CppExpressionKind.ObjCMessage);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_BlockExpr:
                    expression = new CppRawExpression(CppExpressionKind.Block);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_IntegerLiteral:
                    expression = new CppLiteralExpression(CppExpressionKind.IntegerLiteral, GetCursorAsText(cursor));
                    break;
                case CXCursorKind.CXCursor_FloatingLiteral:
                    expression = new CppLiteralExpression(CppExpressionKind.FloatingLiteral, GetCursorAsText(cursor));
                    break;
                case CXCursorKind.CXCursor_ImaginaryLiteral:
                    expression = new CppLiteralExpression(CppExpressionKind.ImaginaryLiteral, GetCursorAsText(cursor));
                    break;
                case CXCursorKind.CXCursor_StringLiteral:
                    expression = new CppLiteralExpression(CppExpressionKind.StringLiteral, GetCursorAsText(cursor));
                    break;
                case CXCursorKind.CXCursor_CharacterLiteral:
                    expression = new CppLiteralExpression(CppExpressionKind.CharacterLiteral, GetCursorAsText(cursor));
                    break;
                case CXCursorKind.CXCursor_ParenExpr:
                    expression = new CppParenExpression();
                    visitChildren = true;
                    break;
                case CXCursorKind.CXCursor_UnaryOperator:
                    var tokens = new Tokenizer(cursor);
                    expression = new CppUnaryExpression(CppExpressionKind.UnaryOperator)
                    {
                        Operator = tokens.Count > 0 ? tokens.GetString(0) : string.Empty
                    };
                    visitChildren = true;
                    break;
                case CXCursorKind.CXCursor_ArraySubscriptExpr:
                    expression = new CppRawExpression(CppExpressionKind.ArraySubscript);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_BinaryOperator:
                    expression = new CppBinaryExpression(CppExpressionKind.BinaryOperator);
                    visitChildren = true;
                    break;
                case CXCursorKind.CXCursor_CompoundAssignOperator:
                    expression = new CppRawExpression(CppExpressionKind.CompoundAssignOperator);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_ConditionalOperator:
                    expression = new CppRawExpression(CppExpressionKind.ConditionalOperator);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CStyleCastExpr:
                    expression = new CppRawExpression(CppExpressionKind.CStyleCast);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CompoundLiteralExpr:
                    expression = new CppRawExpression(CppExpressionKind.CompoundLiteral);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_InitListExpr:
                    expression = new CppInitListExpression();
                    visitChildren = true;
                    break;
                case CXCursorKind.CXCursor_AddrLabelExpr:
                    expression = new CppRawExpression(CppExpressionKind.AddrLabel);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_StmtExpr:
                    expression = new CppRawExpression(CppExpressionKind.Stmt);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_GenericSelectionExpr:
                    expression = new CppRawExpression(CppExpressionKind.GenericSelection);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_GNUNullExpr:
                    expression = new CppRawExpression(CppExpressionKind.GNUNull);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CXXStaticCastExpr:
                    expression = new CppRawExpression(CppExpressionKind.CXXStaticCast);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CXXDynamicCastExpr:
                    expression = new CppRawExpression(CppExpressionKind.CXXDynamicCast);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CXXReinterpretCastExpr:
                    expression = new CppRawExpression(CppExpressionKind.CXXReinterpretCast);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CXXConstCastExpr:
                    expression = new CppRawExpression(CppExpressionKind.CXXConstCast);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CXXFunctionalCastExpr:
                    expression = new CppRawExpression(CppExpressionKind.CXXFunctionalCast);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CXXTypeidExpr:
                    expression = new CppRawExpression(CppExpressionKind.CXXTypeid);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CXXBoolLiteralExpr:
                    expression = new CppRawExpression(CppExpressionKind.CXXBoolLiteral);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CXXNullPtrLiteralExpr:
                    expression = new CppRawExpression(CppExpressionKind.CXXNullPtrLiteral);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CXXThisExpr:
                    expression = new CppRawExpression(CppExpressionKind.CXXThis);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CXXThrowExpr:
                    expression = new CppRawExpression(CppExpressionKind.CXXThrow);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CXXNewExpr:
                    expression = new CppRawExpression(CppExpressionKind.CXXNew);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_CXXDeleteExpr:
                    expression = new CppRawExpression(CppExpressionKind.CXXDelete);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_UnaryExpr:
                    expression = new CppRawExpression(CppExpressionKind.Unary);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_ObjCStringLiteral:
                    expression = new CppRawExpression(CppExpressionKind.ObjCStringLiteral);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_ObjCEncodeExpr:
                    expression = new CppRawExpression(CppExpressionKind.ObjCEncode);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_ObjCSelectorExpr:
                    expression = new CppRawExpression(CppExpressionKind.ObjCSelector);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_ObjCProtocolExpr:
                    expression = new CppRawExpression(CppExpressionKind.ObjCProtocol);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_ObjCBridgedCastExpr:
                    expression = new CppRawExpression(CppExpressionKind.ObjCBridgedCast);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_PackExpansionExpr:
                    expression = new CppRawExpression(CppExpressionKind.PackExpansion);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_SizeOfPackExpr:
                    expression = new CppRawExpression(CppExpressionKind.SizeOfPack);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_LambdaExpr:
                    expression = new CppRawExpression(CppExpressionKind.Lambda);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_ObjCBoolLiteralExpr:
                    expression = new CppRawExpression(CppExpressionKind.ObjCBoolLiteral);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_ObjCSelfExpr:
                    expression = new CppRawExpression(CppExpressionKind.ObjCSelf);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_OMPArraySectionExpr:
                    expression = new CppRawExpression(CppExpressionKind.OMPArraySection);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_ObjCAvailabilityCheckExpr:
                    expression = new CppRawExpression(CppExpressionKind.ObjCAvailabilityCheck);
                    AppendTokensToExpression(cursor, expression);
                    break;
                case CXCursorKind.CXCursor_FixedPointLiteral:
                    expression = new CppLiteralExpression(CppExpressionKind.FixedPointLiteral, GetCursorAsText(cursor));
                    break;
                default:
                    return null;
            }

            AssignSourceSpan(cursor, expression);

            if (visitChildren)
            {
                cursor.VisitChildren(Visit, data);

                CXChildVisitResult Visit(CXCursor listCursor, CXCursor initListCursor, CXClientData clientData)
                {
                    var item = VisitExpression(listCursor, data);

                    if (item != null)
                    {
                        expression.AddArgument(item);
                    }

                    return CXChildVisitResult.CXChildVisit_Continue;
                }
            }

            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_BinaryOperator:
                    var beforeOperatorOffset = expression.Arguments[0].Span.End.Offset;
                    var afterOperatorOffset = expression.Arguments[1].Span.Start.Offset;
                    ((CppBinaryExpression)expression).Operator = GetCursorAsTextBetweenOffset(cursor, beforeOperatorOffset, afterOperatorOffset);
                    break;
            }

            return expression;
        }

        private void AppendTokensToExpression(CXCursor cursor, CppExpression expression)
        {
            if (!(expression is CppRawExpression tokensExpr)) { return; }

            var tokenizer = new Tokenizer(cursor);

            for (int i = 0; i < tokenizer.Count; i++)
            {
                tokensExpr.Tokens.Add(tokenizer[i]);
            }

            tokensExpr.UpdateTextFromTokens();
        }

        private CppEnum VisitEnumDecl(CXCursor cursor, CXClientData data)
        {
            var cppEnum = GetOrCreateDeclarationContainer<CppEnum>(cursor, data, out var context);

            if (cursor.IsDefinition && !context.IsChildrenVisited)
            {
                var integralType = cursor.EnumDecl_IntegerType;
                cppEnum.IntegerType = GetCppType(integralType.Declaration, integralType, cursor, data);
                cppEnum.IsScoped = cursor.EnumDecl_IsScoped;

                context.IsChildrenVisited = true;
                cursor.VisitChildren(VisitMember, data);
            }

            return cppEnum;
        }

        private static CppStorageQualifier GetStorageQualifier(CXCursor cursor)
        {
            switch (cursor.StorageClass)
            {
                case CX_StorageClass.CX_SC_Extern:
                case CX_StorageClass.CX_SC_PrivateExtern:
                    return CppStorageQualifier.Extern;
                case CX_StorageClass.CX_SC_Static:
                    return CppStorageQualifier.Static;
                default:
                    return CppStorageQualifier.None;
            }
        }

        private CppFunction VisitFunctionDecl(CXCursor cursor, CXCursor parent, CXClientData data)
        {
            var contextContainer = GetOrCreateDeclarationContainer(cursor.SemanticParent, data);
            var container = contextContainer.DeclarationContainer;

            if (container == null)
            {
                WarningUnhandled(cursor, parent);
                return null;
            }

            var functionName = GetCursorSpelling(cursor);
            var cppFunction = new CppFunction(functionName)
            {
                Visibility = contextContainer.CurrentVisibility,
                StorageQualifier = GetStorageQualifier(cursor),
                LinkageKind = GetLinkage(cursor.Linkage)
            };

            if (cursor.Kind == CXCursorKind.CXCursor_Constructor)
            {
                var cppClass = (CppClass)container;
                cppFunction.IsConstructor = true;
                cppClass.Constructors.Add(cppFunction);
            }
            else
            {
                container.Functions.Add(cppFunction);
            }

            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_CXXMethod:
                    cppFunction.Flags |= CppFunctionFlags.Method;
                    break;
                case CXCursorKind.CXCursor_Constructor:
                    cppFunction.Flags |= CppFunctionFlags.Constructor;
                    break;
                case CXCursorKind.CXCursor_Destructor:
                    cppFunction.Flags |= CppFunctionFlags.Destructor;
                    break;
            }

            if (cursor.IsFunctionInlined)
            {
                cppFunction.Flags |= CppFunctionFlags.Inline;
            }

            if (cursor.CXXMethod_IsConst)
            {
                cppFunction.Flags |= CppFunctionFlags.Const;
            }

            if (cursor.CXXMethod_IsDefaulted)
            {
                cppFunction.Flags |= CppFunctionFlags.Defaulted;
            }

            if (cursor.CXXMethod_IsVirtual)
            {
                cppFunction.Flags |= CppFunctionFlags.Virtual;
            }

            if (cursor.CXXMethod_IsPureVirtual)
            {
                cppFunction.Flags |= CppFunctionFlags.Pure | CppFunctionFlags.Virtual;
            }

            cppFunction.ReturnType = GetCppType(cursor.ResultType.Declaration, cursor.ResultType, cursor, data);
            cppFunction.Attributes = ParseFunctionAttributes(cursor, cppFunction.Name);
            cppFunction.CallingConvention = GetCallingConvention(cursor.Type);

            CXChildVisitResult Visit(CXCursor argCursor, CXCursor functionCursor, CXClientData clientData)
            {
                switch (argCursor.Kind)
                {
                    case CXCursorKind.CXCursor_ParmDecl:
                        var argName = GetCursorSpelling(argCursor);
                        var parameter = new CppParameter(GetCppType(argCursor.Type.Declaration, argCursor.Type, functionCursor, clientData), argName);

                        cppFunction.Parameters.Add(parameter);

                        // Visit default parameter value
                        VisitInitValue(argCursor, data, out var paramExpr, out var paramValue);
                        parameter.InitValue = paramValue;
                        parameter.InitExpression = paramExpr;
                        break;
                    default:
                        // Don't generate a warning for unsupported cursor
                        break;
                }

                return CXChildVisitResult.CXChildVisit_Continue;
            }

            cursor.VisitChildren(Visit, data);

            return cppFunction;
        }

        private static CppLinkageKind GetLinkage(CXLinkageKind link)
        {
            switch (link)
            {
                case CXLinkageKind.CXLinkage_Invalid:
                    return CppLinkageKind.Invalid;
                case CXLinkageKind.CXLinkage_NoLinkage:
                    return CppLinkageKind.NoLinkage;
                case CXLinkageKind.CXLinkage_Internal:
                    return CppLinkageKind.Internal;
                case CXLinkageKind.CXLinkage_UniqueExternal:
                    return CppLinkageKind.UniqueExternal;
                case CXLinkageKind.CXLinkage_External:
                    return CppLinkageKind.External;
                default:
                    return CppLinkageKind.Invalid;
            }
        }

        private static CppCallingConvention GetCallingConvention(CXType type)
        {
            var callingConv = type.FunctionTypeCallingConv;
            switch (callingConv)
            {
                case CXCallingConv.CXCallingConv_Default:
                    return CppCallingConvention.Default;
                case CXCallingConv.CXCallingConv_C:
                    return CppCallingConvention.C;
                case CXCallingConv.CXCallingConv_X86StdCall:
                    return CppCallingConvention.X86StdCall;
                case CXCallingConv.CXCallingConv_X86FastCall:
                    return CppCallingConvention.X86FastCall;
                case CXCallingConv.CXCallingConv_X86ThisCall:
                    return CppCallingConvention.X86ThisCall;
                case CXCallingConv.CXCallingConv_X86Pascal:
                    return CppCallingConvention.X86Pascal;
                case CXCallingConv.CXCallingConv_AAPCS:
                    return CppCallingConvention.AAPCS;
                case CXCallingConv.CXCallingConv_AAPCS_VFP:
                    return CppCallingConvention.AAPCS_VFP;
                case CXCallingConv.CXCallingConv_X86RegCall:
                    return CppCallingConvention.X86RegCall;
                case CXCallingConv.CXCallingConv_IntelOclBicc:
                    return CppCallingConvention.IntelOclBicc;
                case CXCallingConv.CXCallingConv_Win64:
                    return CppCallingConvention.Win64;
                case CXCallingConv.CXCallingConv_X86_64SysV:
                    return CppCallingConvention.X86_64SysV;
                case CXCallingConv.CXCallingConv_X86VectorCall:
                    return CppCallingConvention.X86VectorCall;
                case CXCallingConv.CXCallingConv_Swift:
                    return CppCallingConvention.Swift;
                case CXCallingConv.CXCallingConv_PreserveMost:
                    return CppCallingConvention.PreserveMost;
                case CXCallingConv.CXCallingConv_PreserveAll:
                    return CppCallingConvention.PreserveAll;
                case CXCallingConv.CXCallingConv_AArch64VectorCall:
                    return CppCallingConvention.AArch64VectorCall;
                case CXCallingConv.CXCallingConv_Invalid:
                    return CppCallingConvention.Invalid;
                case CXCallingConv.CXCallingConv_Unexposed:
                    return CppCallingConvention.Unexposed;
                default:
                    return CppCallingConvention.Unexposed;
            }
        }

        private List<CppAttribute> ParseAttributes(CXCursor cursor)
        {
            List<CppAttribute> attributes = null;
            var tokenizer = new Tokenizer(cursor);
            var tokenIt = new TokenIterator(tokenizer);

            while (tokenIt.CanPeek)
            {
                if (ParseAttributes(tokenIt, ref attributes))
                {
                    continue;
                }

                // If we have a keyword, try to skip it and process following elements
                // for example attribute put right after a struct __declspec(uuid("...")) Test {...}
                if (tokenIt.Peek().Kind == CppTokenKind.Keyword)
                {
                    tokenIt.Next();
                    continue;
                }

                break;
            }

            return attributes;
        }

        private List<CppAttribute> ParseFunctionAttributes(CXCursor cursor, string functionName)
        {
            // TODO: This function is not 100% correct when parsing tokens up to the function name
            // we assume to find the function name immediately followed by a `(`
            // but some return type parameter could actually interfere with that
            // Ideally we would need to parse more properly return type and skip parenthesis for example
            var tokenizer = new Tokenizer(cursor);
            var tokenIt = new TokenIterator(tokenizer);

            // Parse leading attributes
            List<CppAttribute> attributes = null;

            while (tokenIt.CanPeek)
            {
                if (ParseAttributes(tokenIt, ref attributes))
                {
                    continue;
                }

                break;
            }

            if (!tokenIt.CanPeek)
            {
                return attributes;
            }

            // Find function name (We only support simple function name declaration)
            if (!tokenIt.Find(functionName, "("))
            {
                return attributes;
            }

            Debug.Assert(tokenIt.PeekText() == functionName);
            tokenIt.Next();
            Debug.Assert(tokenIt.PeekText() == "(");
            tokenIt.Next();

            int parentCount = 1;

            while (parentCount > 0 && tokenIt.CanPeek)
            {
                var text = tokenIt.PeekText();

                switch (text)
                {
                    case "(":
                        parentCount++;
                        break;
                    case ")":
                        parentCount--;
                        break;
                }

                tokenIt.Next();
            }

            if (parentCount != 0)
            {
                return attributes;
            }

            while (tokenIt.CanPeek)
            {
                if (ParseAttributes(tokenIt, ref attributes))
                {
                    continue;
                }

                // Skip the token if we can parse it.
                tokenIt.Next();
            }

            return attributes;
        }

        private bool ParseAttributes(TokenIterator tokenIt, ref List<CppAttribute> attributes)
        {
            // Parse C++ attributes
            // [[<attribute>]]
            if (tokenIt.Skip("[", "["))
            {
                while (ParseAttribute(tokenIt, out var attribute))
                {
                    if (attributes == null)
                    {
                        attributes = new List<CppAttribute>();
                    }
                    attributes.Add(attribute);

                    tokenIt.Skip(",");
                }

                return tokenIt.Skip("]", "]");
            }

            // Parse GCC or clang attributes
            // __attribute__((<attribute>))
            if (tokenIt.Skip("__attribute__", "(", "("))
            {
                while (ParseAttribute(tokenIt, out var attribute))
                {
                    if (attributes == null)
                    {
                        attributes = new List<CppAttribute>();
                    }
                    attributes.Add(attribute);

                    tokenIt.Skip(",");
                }

                return tokenIt.Skip(")", ")");
            }

            // Parse MSVC attributes
            // __declspec(<attribute>)
            if (tokenIt.Skip("__declspec", "("))
            {
                while (ParseAttribute(tokenIt, out var attribute))
                {
                    if (attributes == null)
                    {
                        attributes = new List<CppAttribute>();
                    }

                    attributes.Add(attribute);

                    tokenIt.Skip(",");
                }

                return tokenIt.Skip(")");
            }

            return false;
        }

        private bool ParseDirectAttribute(CXCursor cursor, ref List<CppAttribute> attributes)
        {
            var tokenizer = new Tokenizer(cursor);
            var tokenIt = new TokenIterator(tokenizer);

            if (ParseAttribute(tokenIt, out var attribute))
            {
                if (attributes == null)
                {
                    attributes = new List<CppAttribute>();
                }

                attributes.Add(attribute);
                return true;
            }

            return false;
        }

        private bool ParseAttribute(TokenIterator tokenIt, out CppAttribute attribute)
        {
            // (identifier ::)? identifier ('(' tokens ')' )? (...)?
            attribute = null;
            var token = tokenIt.Peek();

            if (token == null || !token.Kind.IsIdentifierOrKeyword())
            {
                return false;
            }

            tokenIt.Next(out token);

            var firstToken = token;

            // try (identifier ::)?
            string scope = null;

            if (tokenIt.Skip("::"))
            {
                scope = token.Text;
                token = tokenIt.Peek();

                if (token == null || !token.Kind.IsIdentifierOrKeyword())
                {
                    return false;
                }

                tokenIt.Next(out token);
            }

            // identifier
            string tokenIdentifier = token.Text;
            string arguments = null;

            // ('(' tokens ')' )?
            if (tokenIt.Skip("("))
            {
                var builder = new StringBuilder();
                var previousTokenKind = CppTokenKind.Punctuation;

                while (tokenIt.PeekText() != ")" && tokenIt.Next(out token))
                {
                    if (token.Kind.IsIdentifierOrKeyword() && previousTokenKind.IsIdentifierOrKeyword())
                    {
                        builder.Append(" ");
                    }

                    previousTokenKind = token.Kind;
                    builder.Append(token.Text);
                }

                if (!tokenIt.Skip(")"))
                {
                    return false;
                }

                arguments = builder.ToString();
            }

            var isVariadic = tokenIt.Skip("...");
            var previousToken = tokenIt.PreviousToken();

            attribute = new CppAttribute(tokenIdentifier)
            {
                Span = new CppSourceSpan(firstToken.Span.Start, previousToken.Span.End),
                Scope = scope,
                Arguments = arguments,
                IsVariadic = isVariadic
            };

            return true;
        }

        private CppType VisitTypeDefDecl(CXCursor cursor, CXClientData data)
        {
            var fullTypeDefName = cursor.UnifiedSymbolResolution.CString;

            if (_typedefs.TryGetValue(fullTypeDefName, out var type))
            {
                return type;
            }

            var contextContainer = GetOrCreateDeclarationContainer(cursor.SemanticParent, data);
            var underlyingTypeDefType = GetCppType(cursor.TypedefDeclUnderlyingType.Declaration, cursor.TypedefDeclUnderlyingType, cursor, data);

            var typedefName = GetCursorSpelling(cursor);

            if (AutoSquashTypedef && underlyingTypeDefType is ICppMember cppMember && (string.IsNullOrEmpty(cppMember.Name) || typedefName == cppMember.Name))
            {
                cppMember.Name = typedefName;
                type = (CppType)cppMember;
            }
            else
            {
                var typedef = new CppTypedef(GetCursorSpelling(cursor), underlyingTypeDefType) { Visibility = contextContainer.CurrentVisibility };
                contextContainer.DeclarationContainer.Typedefs.Add(typedef);
                type = typedef;
            }

            // The type could have been added separately as part of the GetCppType above
            if (_typedefs.TryGetValue(fullTypeDefName, out var cppPreviousCppType))
            {
                Debug.Assert(cppPreviousCppType.GetType() == type.GetType());
            }
            else
            {
                _typedefs.Add(fullTypeDefName, type);
            }
            return type;
        }

        private string GetCursorAsText(CXCursor cursor)
        {
            var tokenizer = new Tokenizer(cursor);
            return tokenizer.TokensToString();
        }

        private string GetCursorAsTextBetweenOffset(CXCursor cursor, int startOffset, int endOffset)
        {
            var tokenizer = new Tokenizer(cursor);
            var builder = new StringBuilder();
            var previousTokenKind = CppTokenKind.Punctuation;

            for (int i = 0; i < tokenizer.Count; i++)
            {
                var token = tokenizer[i];

                if (previousTokenKind.IsIdentifierOrKeyword() &&
                    token.Kind.IsIdentifierOrKeyword())
                {
                    builder.Append(" ");
                }

                if (token.Span.Start.Offset >= startOffset &&
                    token.Span.End.Offset <= endOffset)
                {
                    builder.Append(token.Text);
                }

                previousTokenKind = token.Kind;
            }

            return builder.ToString();
        }

        private string GetCursorSpelling(CXCursor cursor) => cursor.Spelling.ToString();

        private CppType GetCppType(CXCursor cursor, CXType type, CXCursor parent, CXClientData data)
        {
            var cppType = GetCppTypeInternal(cursor, type, parent, data);

            return type.IsConstQualified
                ? new CppQualifiedType(CppTypeQualifier.Const, cppType)
                : type.IsVolatileQualified
                    ? new CppQualifiedType(CppTypeQualifier.Volatile, cppType)
                    : cppType;
        }

        private CppType GetCppTypeInternal(CXCursor cursor, CXType type, CXCursor parent, CXClientData data)
        {
            switch (type.kind)
            {
                case CXTypeKind.CXType_Void:
                    return CppPrimitiveType.Void;
                case CXTypeKind.CXType_Bool:
                    return CppPrimitiveType.Bool;
                case CXTypeKind.CXType_UChar:
                    return CppPrimitiveType.UnsignedChar;
                case CXTypeKind.CXType_UShort:
                    return CppPrimitiveType.UnsignedShort;
                case CXTypeKind.CXType_UInt:
                    return CppPrimitiveType.UnsignedInt;
                case CXTypeKind.CXType_ULong:
                    return CppPrimitiveType.UnsignedInt;
                case CXTypeKind.CXType_ULongLong:
                    return CppPrimitiveType.UnsignedLongLong;
                case CXTypeKind.CXType_SChar:
                    return CppPrimitiveType.Char;
                case CXTypeKind.CXType_Char_S:
                    return CppPrimitiveType.Char;
                case CXTypeKind.CXType_WChar:
                    return CppPrimitiveType.WChar;
                case CXTypeKind.CXType_Short:
                    return CppPrimitiveType.Short;
                case CXTypeKind.CXType_Int:
                    return CppPrimitiveType.Int;
                case CXTypeKind.CXType_Long:
                    return CppPrimitiveType.Int;
                case CXTypeKind.CXType_LongLong:
                    return CppPrimitiveType.LongLong;
                case CXTypeKind.CXType_Float:
                    return CppPrimitiveType.Float;
                case CXTypeKind.CXType_Double:
                    return CppPrimitiveType.Double;
                case CXTypeKind.CXType_LongDouble:
                    return CppPrimitiveType.LongDouble;
                case CXTypeKind.CXType_Pointer:
                    return new CppPointerType(GetCppType(type.PointeeType.Declaration, type.PointeeType, parent, data)) { SizeOf = (int)type.SizeOf };
                case CXTypeKind.CXType_LValueReference:
                    return new CppReferenceType(GetCppType(type.PointeeType.Declaration, type.PointeeType, parent, data));
                case CXTypeKind.CXType_Record:
                    return VisitClassDecl(cursor, data);
                case CXTypeKind.CXType_Enum:
                    return VisitEnumDecl(cursor, data);
                case CXTypeKind.CXType_FunctionProto:
                    return VisitFunctionType(cursor, type, parent, data);
                case CXTypeKind.CXType_Typedef:
                    return VisitTypeDefDecl(cursor, data);
                case CXTypeKind.CXType_Elaborated:
                    return GetCppType(type.CanonicalType.Declaration, type.CanonicalType, parent, data);
                case CXTypeKind.CXType_ConstantArray:
                case CXTypeKind.CXType_IncompleteArray:
                    {
                        var elementType = GetCppType(type.ArrayElementType.Declaration, type.ArrayElementType, parent, data);
                        return new CppArrayType(elementType, (int)type.ArraySize);
                    }

                case CXTypeKind.CXType_DependentSizedArray:
                    {
                        // TODO: this is not yet supported
                        _rootCompilation.Diagnostics.Warning($"Dependent sized arrays `{type}` from `{parent}` is not supported", GetSourceLocation(parent.Location));
                        var elementType = GetCppType(type.ArrayElementType.Declaration, type.ArrayElementType, parent, data);
                        return new CppArrayType(elementType, (int)type.ArraySize);
                    }

                case CXTypeKind.CXType_Unexposed:
                    return new CppUnexposedType(type.ToString()) { SizeOf = (int)type.SizeOf };
                case CXTypeKind.CXType_Attributed:
                    return GetCppType(type.ModifierType.Declaration, type.ModifierType, parent, data);
                default:
                    WarningUnhandled(cursor, parent, type);
                    return new CppUnexposedType(type.ToString()) { SizeOf = (int)type.SizeOf };
            }
        }

        private CppFunctionType VisitFunctionType(CXCursor cursor, CXType type, CXCursor parent, CXClientData data)
        {
            // Gets the return type
            var returnType = GetCppType(type.ResultType.Declaration, type.ResultType, cursor, data);

            var cppFunction = new CppFunctionType(returnType)
            {
                CallingConvention = GetCallingConvention(type)
            };

            // We don't use this but use the visitor children to try to recover the parameter names

            //            for (uint i = 0; i < type.NumArgTypes; i++)
            //            {
            //                var argType = type.GetArgType(i);
            //                var cppType = GetCppType(argType.Declaration, argType, type.Declaration, data);
            //                cppFunction.ParameterTypes.Add(cppType);
            //            }

            bool isParsingParameter = false;

            CXChildVisitResult Visit(CXCursor cxCursor, CXCursor _, CXClientData clientData)
            {
                if (cxCursor.Kind == CXCursorKind.CXCursor_ParmDecl)
                {
                    var name = GetCursorSpelling(cxCursor);
                    var parameterType = GetCppType(cxCursor.Type.Declaration, cxCursor.Type, cxCursor, data);

                    cppFunction.Parameters.Add(new CppParameter(parameterType, name));
                    isParsingParameter = true;
                }

                return isParsingParameter ? CXChildVisitResult.CXChildVisit_Continue : CXChildVisitResult.CXChildVisit_Recurse;
            }

            parent.VisitChildren(Visit, data);


            return cppFunction;
        }

        private void Unhandled(CXCursor cursor)
        {
            var cppLocation = GetSourceLocation(cursor.Location);
            _rootCompilation.Diagnostics.Warning($"Unhandled declaration: {cursor}.", cppLocation);
        }

        private void WarningUnhandled(CXCursor cursor, CXCursor parent, CXType type)
        {
            var cppLocation = GetSourceLocation(cursor.Location);

            if (cppLocation.Line == 0)
            {
                cppLocation = GetSourceLocation(parent.Location);
            }

            _rootCompilation.Diagnostics.Warning($"The type `{type}` of kind `{type.KindSpelling}` is not supported in `{parent}`", cppLocation);
        }

        protected void WarningUnhandled(CXCursor cursor, CXCursor parent)
        {
            var cppLocation = GetSourceLocation(cursor.Location);

            if (cppLocation.Line == 0)
            {
                cppLocation = GetSourceLocation(parent.Location);
            }

            _rootCompilation.Diagnostics.Warning($"Unhandled declaration: {cursor} in {parent}.", cppLocation);
        }

        /// <summary>
        /// Internal class to iterate on tokens
        /// </summary>
        private class TokenIterator
        {
            private readonly Tokenizer _tokens;
            private int _index;

            public TokenIterator(Tokenizer tokens)
            {
                _tokens = tokens;
            }

            public bool Skip(string expectedText)
            {
                if (_index < _tokens.Count)
                {
                    if (_tokens.GetString(_index) == expectedText)
                    {
                        _index++;
                        return true;
                    }
                }

                return false;
            }

            public CppToken PreviousToken()
            {
                if (_index > 0)
                {
                    return _tokens[_index - 1];
                }

                return null;
            }

            public bool Skip(params string[] expectedTokens)
            {
                var startIndex = _index;
                foreach (var expectedToken in expectedTokens)
                {
                    if (startIndex < _tokens.Count)
                    {
                        if (_tokens.GetString(startIndex) == expectedToken)
                        {
                            startIndex++;
                            continue;
                        }
                    }
                    return false;
                }
                _index = startIndex;
                return true;
            }

            public bool Find(params string[] expectedTokens)
            {
                var startIndex = _index;
            restart:
                while (startIndex < _tokens.Count)
                {
                    var firstIndex = startIndex;
                    foreach (var expectedToken in expectedTokens)
                    {
                        if (startIndex < _tokens.Count)
                        {
                            if (_tokens.GetString(startIndex) == expectedToken)
                            {
                                startIndex++;
                                continue;
                            }
                        }
                        startIndex = firstIndex + 1;
                        goto restart;
                    }
                    _index = firstIndex;
                    return true;
                }
                return false;
            }

            public bool Next(out CppToken token)
            {
                token = null;
                if (_index < _tokens.Count)
                {
                    token = _tokens[_index];
                    _index++;
                    return true;
                }
                return false;
            }

            public bool CanPeek => _index < _tokens.Count;

            public bool Next()
            {
                if (_index < _tokens.Count)
                {
                    _index++;
                    return true;
                }
                return false;
            }

            public CppToken Peek()
            {
                if (_index < _tokens.Count)
                {
                    return _tokens[_index];
                }
                return null;
            }

            public string PeekText()
            {
                if (_index < _tokens.Count)
                {
                    return _tokens.GetString(_index);
                }
                return null;
            }
        }

        /// <summary>
        /// Internal class to tokenize
        /// </summary>
        [DebuggerTypeProxy(typeof(TokenizerDebuggerType))]
        private class Tokenizer
        {
            private readonly CXToken[] _tokens;
            private CppToken[] _cppTokens;
            private readonly CXTranslationUnit _tu;

            public Tokenizer(CXCursor cursor)
            {
                var range = cursor.Extent;
                _tokens = null;
                var tu = cursor.TranslationUnit;
                tu.Tokenize(range, out _tokens);
                _tu = tu;
            }

            public int Count => _tokens?.Length ?? 0;

            public CppToken this[int i]
            {
                get
                {
                    // Only create a tokenizer if necessary
                    if (_cppTokens == null)
                    {
                        _cppTokens = new CppToken[_tokens.Length];
                    }

                    ref var cppToken = ref _cppTokens[i];

                    if (cppToken != null)
                    {
                        return cppToken;
                    }

                    var token = _tokens[i];
                    CppTokenKind cppTokenKind = 0;

                    switch (token.Kind)
                    {
                        case CXTokenKind.CXToken_Punctuation:
                            cppTokenKind = CppTokenKind.Punctuation;
                            break;
                        case CXTokenKind.CXToken_Keyword:
                            cppTokenKind = CppTokenKind.Keyword;
                            break;
                        case CXTokenKind.CXToken_Identifier:
                            cppTokenKind = CppTokenKind.Identifier;
                            break;
                        case CXTokenKind.CXToken_Literal:
                            cppTokenKind = CppTokenKind.Literal;
                            break;
                        case CXTokenKind.CXToken_Comment:
                            cppTokenKind = CppTokenKind.Comment;
                            break;
                    }

                    var tokenStr = token.GetSpelling(_tu).CString;
                    var tokenRange = token.GetExtent(_tu);

                    cppToken = new CppToken(cppTokenKind, tokenStr)
                    {
                        Span = new CppSourceSpan(GetSourceLocation(tokenRange.Start), GetSourceLocation(tokenRange.End))
                    };

                    return cppToken;
                }
            }

            public string GetString(int i)
            {
                var token = _tokens[i];
                return token.GetSpelling(_tu).CString;
            }

            public string TokensToString()
            {
                if (_tokens == null)
                {
                    return null;
                }

                var tokens = new List<CppToken>(_tokens.Length);

                for (int i = 0; i < _tokens.Length; i++)
                {
                    tokens.Add(this[i]);
                }

                return CppToken.TokensToString(tokens);
            }
        }

        private class TokenizerDebuggerType
        {
            private readonly Tokenizer _tokenizer;

            public TokenizerDebuggerType(Tokenizer tokenizer)
            {
                _tokenizer = tokenizer;
            }

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public object[] Items
            {
                get
                {
                    var array = new object[_tokenizer.Count];
                    for (int i = 0; i < _tokenizer.Count; i++)
                    {
                        array[i] = _tokenizer[i];
                    }
                    return array;
                }
            }
        }

        private class CppContainerContext
        {
            public CppContainerContext(ICppContainer container)
            {
                Container = container;
            }

            public ICppContainer Container;

            public ICppDeclarationContainer DeclarationContainer => Container as ICppDeclarationContainer;

            public CppVisibility CurrentVisibility;

            public bool IsChildrenVisited;
        }
    }
}