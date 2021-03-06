﻿/* Copyright 2015 Brock Reeve
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Pickaxe.Runtime;
using Pickaxe.Sdk;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pickaxe.CodeDom.Visitor
{
    public partial class CodeDomGenerator : IAstVisitor
    {
        private void GenerateSelectOnly(SelectStatement statement)
        {
            var fromDomArg = new CodeDomArg();

            CodeMemberMethod method = new CodeMemberMethod();
            method.Name = "Select_" + fromDomArg.MethodIdentifier;
            method.Attributes = MemberAttributes.Private;
            method.ReturnType = new CodeTypeReference("Table", new CodeTypeReference("ResultRow"));
            GenerateCallStatement(method.Statements, statement.Line.Line);

            _mainType.Type.Members.Add(method);

            var methodStatements = new CodeStatementCollection();


            methodStatements.Add(new CodeVariableDeclarationStatement(new CodeTypeReference("RuntimeTable", new CodeTypeReference("ResultRow")),
                "result",
                new CodeObjectCreateExpression(new CodeTypeReference("RuntimeTable", new CodeTypeReference("ResultRow")))));


            methodStatements.Add(new CodeVariableDeclarationStatement(
               new CodeTypeReference("ResultRow"),
               "resultRow",
               new CodeObjectCreateExpression(new CodeTypeReference("ResultRow"), new CodeSnippetExpression(statement.Args.Length.ToString()))));


            var selectArgAssignments = new List<CodeAssignStatement>();
            for (int x = 0; x < statement.Args.Length; x++) //select args
            {
                var domSelectArg = VisitChild(statement.Args[x], new CodeDomArg() { Scope = fromDomArg.Scope });

                var assignment = new CodeAssignStatement();
                assignment.Left = new CodeIndexerExpression(new CodeTypeReferenceExpression("resultRow"), new CodeSnippetExpression(x.ToString()));
                assignment.Right = domSelectArg.CodeExpression;

                methodStatements.Add(new CodeMethodInvokeExpression(new CodeTypeReferenceExpression("result"), "AddColumn", new CodePrimitiveExpression(((SelectArgsInfo)domSelectArg.Tag).DisplayColumnName)));

                selectArgAssignments.Add(assignment);
            }

            var addResults = new CodeMethodInvokeExpression(
                    new CodeMethodReferenceExpression(new CodeTypeReferenceExpression("result"), "Add"),
                    new CodeArgumentReferenceExpression("resultRow"));

            methodStatements.AddRange(selectArgAssignments.ToArray());
            methodStatements.Add(addResults);
            var callSelect = new CodeExpressionStatement(new CodeMethodInvokeExpression(null, "OnSelect", new CodeVariableReferenceExpression("result")));
            methodStatements.Add(callSelect);
            methodStatements.Add(new CodeMethodReturnStatement(new CodeTypeReferenceExpression("result")));

            method.Statements.AddRange(methodStatements);

            var methodcall = new CodeMethodInvokeExpression(
                new CodeMethodReferenceExpression(null, method.Name));

            _codeStack.Peek().Tag = new Action(() => method.Statements.Remove(callSelect));
            _codeStack.Peek().CodeExpression = methodcall;
            _codeStack.Peek().Scope = new ScopeData<TableDescriptor>() { CodeDomReference = method.ReturnType };
            _codeStack.Peek().ParentStatements.Add(methodcall);
        }


        public void Visit(SelectStatement statement)
        {
            using (Scope.PushSelect())
            {
                if (statement.From == null)
                {
                    GenerateSelectOnly(statement);
                    return;
                }

                var fromDomArg = VisitChild(statement.From);
                var rowType = fromDomArg.Scope.CodeDomReference.TypeArguments[0];

                CodeMemberMethod method = new CodeMemberMethod();
                method.Name = "Select_" + fromDomArg.MethodIdentifier;
                method.Attributes = MemberAttributes.Private;
                method.ReturnType = new CodeTypeReference("Table", new CodeTypeReference("ResultRow"));
                GenerateCallStatement(method.Statements, statement.Line.Line);

                _mainType.Type.Members.Add(method);

                var methodStatements = new CodeStatementCollection();

                methodStatements.Add(new CodeVariableDeclarationStatement(new CodeTypeReference("RuntimeTable", new CodeTypeReference("ResultRow")),
                    "result",
                    new CodeObjectCreateExpression(new CodeTypeReference("RuntimeTable", new CodeTypeReference("ResultRow")))));


                var selectArgAssignments = new List<CodeAssignStatement>();
                if (statement.Args.Length > 0) //visit first one in case select *
                    VisitChild(statement.Args[0], new CodeDomArg() { Scope = fromDomArg.Scope });

                var outerLoopNeeded = false;

                //Needed for both
                for (int x = 0; x < statement.Args.Length; x++) //select args
                {
                    var domSelectArg = VisitChild(statement.Args[x], new CodeDomArg() { Scope = fromDomArg.Scope });
                    if (((SelectArgsInfo)domSelectArg.Tag).IsPickStatement)
                        outerLoopNeeded = true;

                    var assignment = new CodeAssignStatement();
                    assignment.Left = new CodeIndexerExpression(new CodeTypeReferenceExpression("resultRow"), new CodeSnippetExpression(x.ToString()));
                    assignment.Right = domSelectArg.CodeExpression;

                    methodStatements.Add(new CodeMethodInvokeExpression(new CodeTypeReferenceExpression("result"), "AddColumn", new CodePrimitiveExpression(((SelectArgsInfo)domSelectArg.Tag).DisplayColumnName)));
                    selectArgAssignments.Add(assignment);
                }


                methodStatements.Add(new CodeVariableDeclarationStatement(fromDomArg.Scope.CodeDomReference,
                "fromTable",
                fromDomArg.CodeExpression));

                if (statement.Where != null)
                {
                    var domWhereArgs = VisitChild(statement.Where, new CodeDomArg() { Scope = fromDomArg.Scope });
                    methodStatements.Add(new CodeAssignStatement(new CodeVariableReferenceExpression("fromTable"), domWhereArgs.CodeExpression));
                }

                //outside iterator
                methodStatements.Add(new CodeVariableDeclarationStatement(new CodeTypeReference("IEnumerator", rowType),
                    "x",
                new CodeMethodInvokeExpression(
                        new CodeMethodReferenceExpression(new CodeTypeReferenceExpression("fromTable"), "GetEnumerator",
                        null))));


                //foreach loops

                var outerLoop = new CodeIterationStatement();
                outerLoop.InitStatement = new CodeSnippetStatement();
                outerLoop.IncrementStatement = new CodeSnippetStatement();
                outerLoop.TestExpression = new CodeMethodInvokeExpression(
                    new CodeMethodReferenceExpression(new CodeTypeReferenceExpression("x"), "MoveNext",
                    null));

                outerLoop.Statements.Add(new CodeVariableDeclarationStatement(rowType,
                "row",
                new CodePropertyReferenceExpression(new CodeTypeReferenceExpression("x"), "Current")));

                var codeLoop = outerLoop;
                if (outerLoopNeeded)
                {
                    var aliases = Scope.Current.AliasType<DownloadPage>();
                    CodeDomArg args = null;
                    if (aliases.Length == 0)
                    {
                        //register error we could't find a download table and we are trying to access pick statements.
                        //create fake codedomargs
                        args = new CodeDomArg() { CodeExpression = new CodePrimitiveExpression("DownloadTable") };
                    }
                    else
                    {
                        var reference = new TableMemberReference() { Member = "nodes", RowReference = new TableVariableRowReference() { Id = aliases[0] } };
                        args = VisitChild(reference);
                    }

                    //Needed only for DownloadRow
                    outerLoop.Statements.Add(
                        new CodeVariableDeclarationStatement(
                            new CodeTypeReference("IEnumerator", new CodeTypeReference("HtmlElement")),
                            "y",
                            new CodeMethodInvokeExpression(
                                new CodeMethodReferenceExpression(
                                        args.CodeExpression, "GetEnumerator", null))));


                    //Needed only for DownloadRow
                    codeLoop = new CodeIterationStatement();
                    codeLoop.InitStatement = new CodeSnippetStatement();
                    codeLoop.IncrementStatement = new CodeSnippetStatement();
                    codeLoop.TestExpression = new CodeMethodInvokeExpression(
                        new CodeMethodReferenceExpression(new CodeTypeReferenceExpression("y"), "MoveNext",
                        null));

                    //Needed only for DownloadRow
                    codeLoop.Statements.Add(new CodeVariableDeclarationStatement(
                        new CodeTypeReference("HtmlElement"),
                        "node",
                        new CodePropertyReferenceExpression(new CodeTypeReferenceExpression("y"), "Current")));

                    outerLoop.Statements.Add(codeLoop);
                }

                //Needed for both.
                codeLoop.Statements.Add(new CodeVariableDeclarationStatement(
                    new CodeTypeReference("ResultRow"),
                    "resultRow",
                    new CodeObjectCreateExpression(new CodeTypeReference("ResultRow"), new CodeSnippetExpression(statement.Args.Length.ToString()))));

                codeLoop.Statements.AddRange(selectArgAssignments.ToArray());

                var addResults = new CodeMethodInvokeExpression(
                    new CodeMethodReferenceExpression(new CodeTypeReferenceExpression("result"), "Add"),
                    new CodeArgumentReferenceExpression("resultRow"));
                codeLoop.Statements.Add(addResults);
                

                methodStatements.Add(outerLoop);

                if (outerLoopNeeded)
                {
                    var aliases = Scope.Current.AliasType<DownloadPage>();
                    outerLoop.Statements.Add(new CodeMethodInvokeExpression(new CodePropertyReferenceExpression(new CodeVariableReferenceExpression("row"), aliases[0]), "Clear"));
                }

                var callSelect = new CodeExpressionStatement(new CodeMethodInvokeExpression(null, "OnSelect", new CodeVariableReferenceExpression("result")));
                methodStatements.Add(callSelect);
                methodStatements.Add(new CodeMethodReturnStatement(new CodeTypeReferenceExpression("result")));
                method.Statements.AddRange(methodStatements);

                var methodcall = new CodeMethodInvokeExpression(
                    new CodeMethodReferenceExpression(null, method.Name));

                _codeStack.Peek().Tag = new Action(() => method.Statements.Remove(callSelect));
                _codeStack.Peek().CodeExpression = methodcall;
                _codeStack.Peek().Scope = new ScopeData<TableDescriptor>() { CodeDomReference = method.ReturnType };
                _codeStack.Peek().ParentStatements.Add(methodcall);
            }
        }
    }
}
