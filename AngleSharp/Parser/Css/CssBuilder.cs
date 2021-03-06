﻿namespace AngleSharp.Parser.Css
{
    using AngleSharp.Css;
    using AngleSharp.Css.Conditions;
    using AngleSharp.Css.MediaFeatures;
    using AngleSharp.Css.Values;
    using AngleSharp.Dom;
    using AngleSharp.Dom.Collections;
    using AngleSharp.Dom.Css;
    using AngleSharp.Extensions;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    /// <summary>
    /// See http://dev.w3.org/csswg/css-syntax/#parsing for details.
    /// </summary>
    [DebuggerStepThrough]
    sealed class CssBuilder
    {
        #region Fields

        readonly CssTokenizer _tokenizer;
        readonly CssParser _parser;
        readonly Stack<CssNode> _nodes;

        #endregion

        #region ctor

        public CssBuilder(CssTokenizer tokenizer, CssParser parser)
        {
            _tokenizer = tokenizer;
            _parser = parser;
            _nodes = new Stack<CssNode>();

            if (parser.Options.IsStoringTrivia)
                _nodes.Push(new CssNode());
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the token container.
        /// </summary>
        public CssNode Container
        {
            get { return _nodes.Count > 0 ? _nodes.Peek() : null; }
        }

        #endregion

        #region Create Rules

        /// <summary>
        /// Parses an @-rule with the given name, if there is any.
        /// </summary>
        public CssRule CreateAtRule(CssToken token)
        {
            if (token.Data.Is(RuleNames.Media))
                return CreateMedia(token);
            else if (token.Data.Is(RuleNames.FontFace))
                return CreateFontFace(token);
            else if (token.Data.Is(RuleNames.Keyframes))
                return CreateKeyframes(token);
            else if (token.Data.Is(RuleNames.Import))
                return CreateImport(token);
            else if (token.Data.Is(RuleNames.Charset))
                return CreateCharset(token);
            else if (token.Data.Is(RuleNames.Namespace))
                return CreateNamespace(token);
            else if (token.Data.Is(RuleNames.Page))
                return CreatePage(token);
            else if (token.Data.Is(RuleNames.Supports))
                return CreateSupports(token);
            else if (token.Data.Is(RuleNames.ViewPort))
                return CreateViewport(token);
            else if (token.Data.Is(RuleNames.Document))
                return CreateDocument(token);

            return CreateUnknown(token);
        }

        /// <summary>
        /// Creates a rule with the enumeration of tokens.
        /// </summary>
        public CssRule CreateRule(CssToken token)
        {
            switch (token.Type)
            {
                case CssTokenType.AtKeyword:
                    return CreateAtRule(token);

                case CssTokenType.CurlyBracketOpen:
                    RaiseErrorOccurred(CssParseError.InvalidBlockStart, token);
                    SkipRule(token);
                    return null;

                case CssTokenType.String:
                case CssTokenType.Url:
                case CssTokenType.CurlyBracketClose:
                case CssTokenType.RoundBracketClose:
                case CssTokenType.SquareBracketClose:
                    RaiseErrorOccurred(CssParseError.InvalidToken, token);
                    SkipRule(token);
                    return null;

                default:
                    return CreateStyle(token);
            }
        }
        
        public CssRule CreateCharset(CssToken current)
        {
            var rule = new CssCharsetRule(_parser);
            var token = NextToken();
            CollectTrivia(ref token);

            if (token.Type == CssTokenType.String)
                rule.CharacterSet = token.Data;

            JumpToEnd(token);
            return rule;
        }

        public CssRule CreateDocument(CssToken current)
        {
            var rule = new CssDocumentRule(_parser);
            var token = NextToken();
            CollectTrivia(ref token);
            FillFunctions(rule.Conditions, ref token);
            CollectTrivia(ref token);

            if (token.Type != CssTokenType.CurlyBracketOpen)
                return SkipDeclarations(token);

            FillRules(rule);
            return rule;
        }

        public CssRule CreateViewport(CssToken current)
        {
            var rule = new CssViewportRule(_parser);
            var token = NextToken();
            CollectTrivia(ref token);

            if (token.Type != CssTokenType.CurlyBracketOpen)
                return SkipDeclarations(token);

            FillDeclarations(rule, Factory.Properties.CreateViewport);
            return rule;
        }

        public CssRule CreateFontFace(CssToken current)
        {
            var rule = new CssFontFaceRule(_parser);
            var token = NextToken();
            CollectTrivia(ref token);

            if (token.Type != CssTokenType.CurlyBracketOpen)
                return SkipDeclarations(token);

            FillDeclarations(rule, Factory.Properties.CreateFont);
            return rule;
        }

        public CssRule CreateImport(CssToken current)
        {
            var rule = new CssImportRule(_parser);
            var token = NextToken();
            CollectTrivia(ref token);

            if (token.Is(CssTokenType.String, CssTokenType.Url))
            {
                rule.Href = token.Data;
                token = NextToken();
                CollectTrivia(ref token);
                FillMediaList(rule.Media, CssTokenType.Semicolon, ref token);
            }

            CollectTrivia(ref token);
            JumpToEnd(token);
            return rule;
        }

        public CssRule CreateKeyframes(CssToken current)
        {
            var rule = new CssKeyframesRule(_parser);
            var token = NextToken();
            CollectTrivia(ref token);
            rule.Name = GetRuleName(ref token);
            CollectTrivia(ref token);

            if (token.Type != CssTokenType.CurlyBracketOpen)
                return SkipDeclarations(token);

            FillKeyframeRules(rule);
            return rule;
        }

        public CssRule CreateMedia(CssToken current)
        {
            var rule = new CssMediaRule(_parser);
            var token = NextToken();
            CollectTrivia(ref token);
            FillMediaList(rule.Media, CssTokenType.CurlyBracketOpen, ref token);
            CollectTrivia(ref token);

            if (token.Type != CssTokenType.CurlyBracketOpen)
            {
                while (token.Type != CssTokenType.Eof)
                {
                    if (token.Type == CssTokenType.Semicolon)
                        return null;
                    else if (token.Type == CssTokenType.CurlyBracketOpen)
                        break;

                    token = NextToken();
                }
            }

            FillRules(rule);
            return rule;
        }

        public CssRule CreateNamespace(CssToken current)
        {
            var rule = new CssNamespaceRule(_parser);
            var token = NextToken();
            CollectTrivia(ref token);
            rule.Prefix = GetRuleName(ref token);
            CollectTrivia(ref token);

            if (token.Type == CssTokenType.Url)
                rule.NamespaceUri = token.Data;

            JumpToEnd(token);
            return rule;
        }

        public CssRule CreatePage(CssToken current)
        {
            var rule = new CssPageRule(_parser);
            var token = NextToken();
            CollectTrivia(ref token);
            rule.Selector = CreateSelector(ref token);
            CollectTrivia(ref token);

            if (token.Type != CssTokenType.CurlyBracketOpen)
                return SkipDeclarations(token);

            FillDeclarations(rule.Style);
            return rule;
        }

        public CssRule CreateSupports(CssToken current)
        {
            var rule = new CssSupportsRule(_parser);
            var token = NextToken();
            CollectTrivia(ref token);
            rule.Condition = AggregateCondition(ref token);
            CollectTrivia(ref token);

            if (token.Type != CssTokenType.CurlyBracketOpen)
                return SkipDeclarations(token);

            FillRules(rule);
            return rule;
        }

        public CssRule CreateStyle(CssToken current)
        {
            var rule = new CssStyleRule(_parser);
            CollectTrivia(ref current);
            rule.Selector = CreateSelector(ref current);
            FillDeclarations(rule.Style);
            return rule.Selector != null ? rule : null;
        }

        public CssKeyframeRule CreateKeyframeRule(CssToken current)
        {
            var rule = new CssKeyframeRule(_parser);
            CollectTrivia(ref current);
            rule.Key = CreateKeyframeSelector(ref current);
            CollectTrivia(ref current);
            FillDeclarations(rule.Style);
            return rule;
        }

        public CssRule CreateUnknown(CssToken current)
        {
            var rule = default(CssUnknownRule);

            if (_parser.Options.IsIncludingUnknownRules)
            {
                var token = NextToken();
                rule = new CssUnknownRule(current.Data, _parser);

                while (token.IsNot(CssTokenType.CurlyBracketOpen, CssTokenType.Semicolon, CssTokenType.Eof))
                {
                    rule.Prelude.Add(token);
                    token = NextToken();
                }

                if (token.Type != CssTokenType.Eof)
                {
                    rule.Content.Add(token);

                    if (token.Type == CssTokenType.CurlyBracketOpen)
                    {
                        var curly = 1;

                        do
                        {
                            token = NextToken();
                            rule.Content.Add(token);

                            switch (token.Type)
                            {
                                case CssTokenType.CurlyBracketOpen:
                                    curly++;
                                    break;
                                case CssTokenType.CurlyBracketClose:
                                    curly--;
                                    break;
                                case CssTokenType.Eof:
                                    curly = 0;
                                    break;
                            }
                        }
                        while (curly != 0);
                    }
                }
            }
            else
            {
                RaiseErrorOccurred(CssParseError.UnknownAtRule, current);
                SkipRule(current);
            }

            return rule;
        }

        #endregion

        #region API

        /// <summary>
        /// Creates a single value. Does not care about the !important flag.
        /// </summary>
        public CssValue CreateValue(ref CssToken token)
        {
            var important = false;
            return CreateValue(CssTokenType.CurlyBracketClose, ref token, out important);
        }

        /// <summary>
        /// Creates a list of CssMedium objects.
        /// </summary>
        public List<CssMedium> CreateMedia(ref CssToken token)
        {
            var list = new List<CssMedium>();
            CollectTrivia(ref token);

            while (token.Type != CssTokenType.Eof)
            {
                CreateNewNode();
                var medium = CreateMedium(ref token);

                if (medium == null || token.IsNot(CssTokenType.Comma, CssTokenType.Eof))
                    throw new DomException(DomError.Syntax);

                token = NextToken();
                CollectTrivia(ref token);
                list.Add(CloseNode(medium));
            }

            return list;
        }

        /// <summary>
        /// Creates as many rules as possible.
        /// </summary>
        /// <returns>The found rules.</returns>
        public void CreateRules(CssStyleSheet sheet)
        {
            var token = NextToken();
            CollectTrivia(ref token);

            while (token.Type != CssTokenType.Eof)
            {
                CreateNewNode();
                var rule = CreateRule(token);
                token = NextToken();
                CollectTrivia(ref token);
                sheet.AddRule(CloseNode(rule));
            }
        }

        /// <summary>
        /// Called before any token in the value regime had been seen.
        /// </summary>
        public CssCondition CreateCondition(ref CssToken token)
        {
            CollectTrivia(ref token);
            return AggregateCondition(ref token);
        }

        /// <summary>
        /// Called in the text for a frame in the @keyframes rule.
        /// </summary>
        public KeyframeSelector CreateKeyframeSelector(ref CssToken token)
        {
            var keys = new List<Percent>();
            var valid = true;
            var start = token;
            CreateNewNode();
            CollectTrivia(ref token);

            while (token.Type != CssTokenType.Eof)
            {
                if (keys.Count > 0)
                {
                    if (token.Type == CssTokenType.CurlyBracketOpen)
                        break;
                    else if (token.Type != CssTokenType.Comma)
                        valid = false;
                    else
                        token = NextToken();

                    CollectTrivia(ref token);
                }

                if (token.Type == CssTokenType.Percentage)
                    keys.Add(new Percent(((CssUnitToken)token).Value));
                else if (token.Type == CssTokenType.Ident && token.Data.Is(Keywords.From))
                    keys.Add(Percent.Zero);
                else if (token.Type == CssTokenType.Ident && token.Data.Is(Keywords.To))
                    keys.Add(Percent.Hundred);
                else
                    valid = false;

                token = NextToken();
                CollectTrivia(ref token);
            }

            if (!valid)
                RaiseErrorOccurred(CssParseError.InvalidSelector, start);

            return CloseNode(new KeyframeSelector(keys));
        }

        /// <summary>
        /// Called when the document functions have to been found.
        /// </summary>
        public List<CssDocumentFunction> CreateFunctions(ref CssToken token)
        {
            var functions = new List<CssDocumentFunction>();
            CollectTrivia(ref token);
            FillFunctions(functions, ref token);
            return functions;
        }

        /// <summary>
        /// Fills the given parent style with declarations given by the tokens.
        /// </summary>
        public void FillDeclarations(CssStyleDeclaration style)
        {
            var token = NextToken();
            CollectTrivia(ref token);

            while (token.IsNot(CssTokenType.Eof, CssTokenType.CurlyBracketClose))
            {
                var property = CreateDeclarationWith(Factory.Properties.Create, ref token);

                if (property != null && property.HasValue)
                    style.SetProperty(property);

                CollectTrivia(ref token);
            }
        }

        /// <summary>
        /// Called before the property name has been detected.
        /// </summary>
        public CssProperty CreateDeclarationWith(Func<String, CssProperty> createProperty, ref CssToken token)
        {
            var property = default(CssProperty);
            CreateNewNode();

            var sb = Pool.NewStringBuilder();

            while (token.Type != CssTokenType.Eof &&
                   token.Type != CssTokenType.Colon &&
                   token.Type != CssTokenType.Whitespace &&
                   token.Type != CssTokenType.Comment &&
                   token.Type != CssTokenType.CurlyBracketOpen &&
                   token.Type != CssTokenType.Semicolon)
            {
                sb.Append(token.ToValue());
                token = NextToken();
            }

            var propertyName = sb.ToPool();

            if (propertyName.Length > 0)
            {
                property = _parser.Options.IsIncludingUnknownDeclarations || 
                           _parser.Options.IsToleratingInvalidValues ?
                    new CssUnknownProperty(propertyName) : createProperty(propertyName);

                if (property == null)
                    RaiseErrorOccurred(CssParseError.UnknownDeclarationName, token);

                CollectTrivia(ref token);

                if (token.Type == CssTokenType.Colon)
                {
                    var important = false;
                    var value = CreateValue(CssTokenType.CurlyBracketClose, ref token, out important);

                    if (value == null)
                        RaiseErrorOccurred(CssParseError.ValueMissing, token);
                    else if (property != null && property.TrySetValue(value))
                        property.IsImportant = important;

                    CollectTrivia(ref token);
                }
                else
                    RaiseErrorOccurred(CssParseError.ColonMissing, token);

                JumpToDeclEnd(ref token);
            }
            else if (token.Type != CssTokenType.Eof)
            {
                RaiseErrorOccurred(CssParseError.IdentExpected, token);
                JumpToDeclEnd(ref token);
            }

            if (token.Type == CssTokenType.Semicolon)
                token = NextToken();

            return CloseNode(property);
        }

        /// <summary>
        /// Called before the property name has been detected.
        /// </summary>
        public CssProperty CreateDeclaration(ref CssToken token)
        {
            CollectTrivia(ref token);
            return CreateDeclarationWith(Factory.Properties.Create, ref token);
        }

        /// <summary>
        /// Scans the current medium for the @media or @import rule.
        /// </summary>
        public CssMedium CreateMedium(ref CssToken token)
        {
            var medium = new CssMedium();
            CollectTrivia(ref token);

            if (token.Type == CssTokenType.Ident)
            {
                var identifier = token.Data;

                if (identifier.Isi(Keywords.Not))
                {
                    medium.IsInverse = true;
                    token = NextToken();
                    CollectTrivia(ref token);
                }
                else if (identifier.Isi(Keywords.Only))
                {
                    medium.IsExclusive = true;
                    token = NextToken();
                    CollectTrivia(ref token);
                }
            }

            if (token.Type == CssTokenType.Ident)
            {
                medium.Type = token.Data;
                token = NextToken();
                CollectTrivia(ref token);

                if (token.Type != CssTokenType.Ident || !token.Data.Isi(Keywords.And))
                    return medium;

                token = NextToken();
                CollectTrivia(ref token);
            }

            do
            {
                if (token.Type != CssTokenType.RoundBracketOpen)
                    return null;

                token = NextToken();
                CollectTrivia(ref token);
                CreateNewNode();
                var feature = CloseNode(CreateFeature(ref token));

                if (feature != null)
                    medium.AddConstraint(feature);

                if (token.Type != CssTokenType.RoundBracketClose)
                    return null;

                token = NextToken();
                CollectTrivia(ref token);

                if (feature == null)
                    return null;

                if (token.Type != CssTokenType.Ident || !token.Data.Isi(Keywords.And))
                    break;

                token = NextToken();
                CollectTrivia(ref token);
            }
            while (token.Type != CssTokenType.Eof);

            return medium;
        }

        #endregion

        #region Helpers

        void SkipRule(CssToken current)
        {
            var scopes = 0;

            while (current.Type != CssTokenType.Eof)
            {
                if (current.Type == CssTokenType.CurlyBracketOpen)
                    scopes++;
                else if (current.Type == CssTokenType.CurlyBracketClose)
                    scopes--;

                if (scopes <= 0 && (current.Is(CssTokenType.Semicolon, CssTokenType.CurlyBracketClose)))
                    break;

                current = NextToken();
            }
        }

        void JumpToEnd(CssToken current)
        {
            while (current.IsNot(CssTokenType.Eof, CssTokenType.Semicolon))
            {
                current = NextToken();
            }
        }

        void JumpToArgEnd(ref CssToken current)
        {
            var arguments = 0;

            while (current.Type != CssTokenType.Eof)
            {
                if (current.Type == CssTokenType.RoundBracketOpen)
                    arguments++;
                else if (arguments <= 0 && (current.Type == CssTokenType.RoundBracketClose))
                    break;
                else if (current.Type == CssTokenType.RoundBracketClose)
                    arguments--;

                current = NextToken();
            }
        }

        void JumpToDeclEnd(ref CssToken current)
        {
            var scopes = 0;

            while (current.Type != CssTokenType.Eof)
            {
                if (current.Type == CssTokenType.CurlyBracketOpen)
                    scopes++;
                else if (scopes <= 0 && (current.Is(CssTokenType.CurlyBracketClose, CssTokenType.Semicolon)))
                    break;
                else if (current.Type == CssTokenType.CurlyBracketClose)
                    scopes--;

                current = NextToken();
            }
        }

        CssToken NextToken()
        {
            var token = _tokenizer.Get();

            if (_nodes.Count > 0)
                _nodes.Peek().Tokens.Add(token);

            return token;
        }

        CssNode CreateNewNode()
        {
            var node = default(CssNode);

            if (_parser.Options.IsStoringTrivia)
            {
                var tokens = _nodes.Peek().Tokens;
                node = new CssNode();

                if (tokens.Count > 0)
                {
                    node.Tokens.Add(tokens[tokens.Count - 1]);
                    tokens.RemoveAt(tokens.Count - 1);
                }

                _nodes.Peek().Children.Add(node);
                _nodes.Push(node);
            }

            return node;
        }

        T CloseNode<T>(T entity)
            where T : IStyleFormattable
        {
            if (_nodes.Count > 0)
            {
                var node = _nodes.Pop();
                var tokens = node.Tokens;
                node.Entity = entity;

                if (tokens.Count > 0)
                {
                    _nodes.Peek().Tokens.Add(tokens[tokens.Count - 1]);
                    tokens.RemoveAt(tokens.Count - 1);
                }
            }

            return entity;

        }

        void CollectTrivia(ref CssToken token)
        {
            if (_nodes.Count > 0)
            {
                StoreTrivia(ref token);
            }
            else
            {
                RemoveTrivia(ref token);
            }
        }

        void StoreTrivia(ref CssToken token)
        {
            var tokens = _nodes.Peek().Tokens;

            while (token.Type == CssTokenType.Whitespace || token.Type == CssTokenType.Comment || token.Type == CssTokenType.Cdc || token.Type == CssTokenType.Cdo)
            {
                token = _tokenizer.Get();
                tokens.Add(token);
            }
        }

        void RemoveTrivia(ref CssToken token)
        {
            while (token.Type == CssTokenType.Whitespace || token.Type == CssTokenType.Comment || token.Type == CssTokenType.Cdc || token.Type == CssTokenType.Cdo)
            {
                token = _tokenizer.Get();
            }
        }     

        CssRule SkipDeclarations(CssToken token)
        {
            RaiseErrorOccurred(CssParseError.InvalidToken, token);
            SkipRule(token);
            return default(CssRule);
        }

        void RaiseErrorOccurred(CssParseError code, CssToken token)
        {
            _tokenizer.RaiseErrorOccurred(code, token.Position);
        }

        #endregion

        #region Conditions

        CssCondition AggregateCondition(ref CssToken token)
        {
            var condition = ExtractCondition(ref token);

            if (condition != null)
            {
                CollectTrivia(ref token);
                var conjunction = token.Data;
                var creator = conjunction.GetCreator();

                if (creator != null)
                {
                    token = NextToken();
                    CollectTrivia(ref token);
                    CreateNewNode();
                    var conditions = MultipleConditions(condition, conjunction, ref token);
                    condition = CloseNode(creator(conditions));
                }
            }

            return condition;
        }

        CssCondition ExtractCondition(ref CssToken token)
        {
            var condition = default(CssCondition);
            CreateNewNode();

            if (token.Type == CssTokenType.RoundBracketOpen)
            {
                token = NextToken();
                CollectTrivia(ref token);
                condition = AggregateCondition(ref token);

                if (condition != null)
                    condition = new GroupCondition(condition);
                else if (token.Type == CssTokenType.Ident)
                    condition = DeclarationCondition(ref token);

                if (token.Type == CssTokenType.RoundBracketClose)
                {
                    token = NextToken();
                    CollectTrivia(ref token);
                }
            }
            else if (token.Data.Isi(Keywords.Not))
            {
                token = NextToken();
                CollectTrivia(ref token);
                condition = ExtractCondition(ref token);

                if (condition != null)
                    condition = new NotCondition(condition);
            }

            return CloseNode(condition);
        }

        CssCondition DeclarationCondition(ref CssToken token)
        {
            var property = Factory.Properties.Create(token.Data) ?? new CssUnknownProperty(token.Data);
            var declaration = default(DeclarationCondition);
            CreateNewNode();
            token = NextToken();
            CollectTrivia(ref token);

            if (token.Type == CssTokenType.Colon)
            {
                var important = false;
                var result = CreateValue(CssTokenType.RoundBracketClose, ref token, out important);
                property.IsImportant = important;

                if (result != null)
                    declaration = new DeclarationCondition(property, result);
            }

            return CloseNode(declaration);
        }

        List<CssCondition> MultipleConditions(CssCondition condition, String connector, ref CssToken token)
        {
            var list = new List<CssCondition>();
            CollectTrivia(ref token);
            list.Add(condition);

            while (token.Type != CssTokenType.Eof)
            {
                condition = ExtractCondition(ref token);

                if (condition == null)
                    break;

                list.Add(condition);

                if (!token.Data.Isi(connector))
                    break;

                token = NextToken();
                CollectTrivia(ref token);
            }

            return list;
        }

        #endregion

        #region Fill Inner

        void FillFunctions(List<CssDocumentFunction> functions, ref CssToken token)
        {
            do
            {
                CreateNewNode();
                var function = token.ToDocumentFunction();

                if (function == null)
                    break;

                token = NextToken();
                CollectTrivia(ref token);
                functions.Add(CloseNode(function));

                if (token.Type != CssTokenType.Comma)
                    break;

                token = NextToken();
                CollectTrivia(ref token);
            }
            while (token.Type == CssTokenType.Eof);
        }

        void FillKeyframeRules(CssKeyframesRule parentRule)
        {
            var token = NextToken();
            CollectTrivia(ref token);

            while (token.IsNot(CssTokenType.Eof, CssTokenType.CurlyBracketClose))
            {
                CreateNewNode();
                var rule = CreateKeyframeRule(token);
                token = NextToken();
                CollectTrivia(ref token);
                parentRule.AddRule(CloseNode(rule));
            }
        }

        void FillDeclarations(CssDeclarationRule rule, Func<String, CssProperty> createProperty)
        {
            var token = NextToken();
            CollectTrivia(ref token);

            while (token.IsNot(CssTokenType.Eof, CssTokenType.CurlyBracketClose))
            {
                var property = CreateDeclarationWith(createProperty, ref token);

                if (property != null && property.HasValue)
                    rule.SetProperty(property);

                CollectTrivia(ref token);
            }
        }

        void FillRules(CssGroupingRule group)
        {
            var token = NextToken();
            CollectTrivia(ref token);

            while (token.IsNot(CssTokenType.Eof, CssTokenType.CurlyBracketClose))
            {
                CreateNewNode();
                var rule = CreateRule(token);
                token = NextToken();
                CollectTrivia(ref token);
                group.AddRule(CloseNode(rule));
            }
        }

        void FillMediaList(MediaList list, CssTokenType end, ref CssToken token)
        {
            if (token.Type == end)
                return;

            while (token.Type != CssTokenType.Eof)
            {
                CreateNewNode();
                var medium = CloseNode(CreateMedium(ref token));

                if (medium != null)
                    list.Add(medium);

                if (token.Type != CssTokenType.Comma)
                    break;

                token = NextToken();
                CollectTrivia(ref token);
            }

            if (token.Type == end && list.Length > 0)
                return;

            list.Clear();
            list.Add(new CssMedium
            {
                IsInverse = true,
                Type = Keywords.All
            });
        }

        #endregion

        #region Create Values

        ISelector CreateSelector(ref CssToken token)
        {
            var selector = Pool.NewSelectorConstructor();
            var start = token;
            CreateNewNode();

            while (token.IsNot(CssTokenType.Eof, CssTokenType.CurlyBracketOpen, CssTokenType.CurlyBracketClose))
            {
                selector.Apply(token);
                token = NextToken();
            }

            var result = selector.ToPool();

            if (!selector.IsValid && !_parser.Options.IsToleratingInvalidValues)
            {
                RaiseErrorOccurred(CssParseError.InvalidSelector, start);
                result = null;
            }

            return CloseNode(result);
        }

        CssValue CreateValue(CssTokenType closing, ref CssToken token, out Boolean important)
        {
            var value = Pool.NewValueBuilder();
            _tokenizer.IsInValue = true;
            token = NextToken();
            var start = token;
            CreateNewNode();

            while (token.Type != CssTokenType.Eof)
            {
                if (token.Is(CssTokenType.Semicolon, closing))
                    break;

                value.Apply(token);
                token = NextToken();
            }

            important = value.IsImportant;
            _tokenizer.IsInValue = false;
            var result = value.ToPool();

            if (!value.IsValid && !_parser.Options.IsToleratingInvalidValues)
            {
                RaiseErrorOccurred(CssParseError.InvalidValue, start);
                result = null;
            }

            return CloseNode(result);
        }

        String GetRuleName(ref CssToken token)
        {
            var name = String.Empty;

            if (token.Type == CssTokenType.Ident)
            {
                name = token.Data;
                token = NextToken();
            }

            return name;
        }

        MediaFeature CreateFeature(ref CssToken token)
        {
            if (token.Type == CssTokenType.Ident)
            {
                var val = CssValue.Empty;
                var feature = _parser.Options.IsToleratingInvalidConstraints ?
                    new UnknownMediaFeature(token.Data) : Factory.MediaFeatures.Create(token.Data);

                token = NextToken();

                if (token.Type == CssTokenType.Colon)
                {
                    var value = Pool.NewValueBuilder();
                    token = NextToken();

                    while (token.Type != CssTokenType.RoundBracketClose || value.IsReady == false)
                    {
                        if (token.Type == CssTokenType.Eof)
                            break;

                        value.Apply(token);
                        token = NextToken();
                    }

                    val = value.ToPool();
                }
                else if (token.Type == CssTokenType.Eof)
                    return null;

                if (feature != null && feature.TrySetValue(val))
                    return feature;
            }
            else
                JumpToArgEnd(ref token);

            return null;
        }

        #endregion
    }
}
