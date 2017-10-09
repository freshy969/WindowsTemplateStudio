// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ApiAnalysis;
using Microsoft.Templates.Core;
using Microsoft.Templates.Core.Composition;
using Microsoft.Templates.Core.Gen;
using Newtonsoft.Json;

namespace TemplateValidator
{
    public static class TemplateJsonVerifier
    {
        private static readonly SimpleJsonAnalyzer Analyzer = new SimpleJsonAnalyzer();
        private static readonly string AllGood = Analyzer.MessageBuilder.AllGoodMessage;
        private static readonly string[] BoolStrings = { "true", "false" };

        // Verify the contents of the config file at the specified path
        public static async Task<VerifierResult> VerifyTemplatePathAsync(string configFilePath)
        {
            var results = new List<string>();

            if (configFilePath == null)
            {
                results.Add("Path to template.json file not provided.");
            }

            if (Path.GetFileName(configFilePath) != "template.json")
            {
                results.Add("Path does not point to a template.json file.");
            }

            // handle relative and absolute paths
            var rootedFilePath = configFilePath;

            if (configFilePath != null && !Path.IsPathRooted(configFilePath))
            {
                rootedFilePath = new FileInfo(configFilePath).FullName;
            }

            if (!File.Exists(rootedFilePath))
            {
                results.Add("Path to template.json file does not exist.");
            }

            if (!results.Any())
            {
                var fileContents = File.ReadAllText(configFilePath);

                // The analyzer compares the JSON with the POCO type. It identifies discrepencies in types, missing or extra properties, etc.
                var analyzerResults = await Analyzer.AnalyzeJsonAsync(fileContents, typeof(ValidationTemplateInfo));

                // The "other" checks are specific to what the wizard does with the config file and expectations of the content
                var otherResults = await PerformOtherTemplateContentChecks(configFilePath, fileContents);

                results = new List<string>(analyzerResults);

                if (otherResults.Any())
                {
                    if (analyzerResults.First() == AllGood)
                    {
                        results = otherResults;
                    }
                    else
                    {
                        results.AddRange(otherResults);
                    }
                }
            }

            var success = results.Count == 1 && results.First() == AllGood;

            return new VerifierResult(success, results);
        }

        private static async Task<List<string>> PerformOtherTemplateContentChecks(string filePath, string fileContents)
        {
            var results = new List<string>();

            try
            {
                var template = JsonConvert.DeserializeObject<ValidationTemplateInfo>(fileContents);

                // Composition templates don't need as much as Page and feature ones
                if (!filePath.Contains("_composition"))
                {
                    EnsureAdequateDescription(template, results);

                    // Composition templates don't need identities
                    EnsureVisualBasicTemplatesAreIdentifiedAppropriately(template, filePath, results);
                }

                EnsureClassificationAsExpected(template, results);

                VerifyTagUsage(template, results);

                var templateRoot = filePath.Replace("\\.template.config\\template.json", string.Empty);

                EnsureAllDefinedPrimaryOutputsExist(template, templateRoot, results);

                EnsureAllDefinedGuidsAreUsed(template, templateRoot, results);

                VerifySymbols(template, results);
            }
            catch (Exception ex)
            {
                results.Add($"Exception during template checks: {ex}");
            }

            await Task.CompletedTask;

            return results;
        }

        private static void VerifySymbols(ValidationTemplateInfo template, List<string> results)
        {
            var type = typeof(GenParams);
            var paramValues = type.GetFields(BindingFlags.Static | BindingFlags.Public)
                                  .Where(f => f.IsLiteral)
                                  .Select(f => f.GetValue(null).ToString())
                                  .ToList();

            // The explicit values here are the ones that are currently in use.
            // In theory any string could be exported and used as a symbol but currently it's only these
            // If lots of tempaltes start exporting new symbols it might be necessary to change how symbol keys are verified
            var allValidSymbolKeys = new List<string>(paramValues) { "baseclass", "setter" };

            foreach (var symbol in template.Symbols)
            {
                if (!allValidSymbolKeys.Contains(symbol.Key))
                {
                    results.Add($"Invalid Symbol key '{symbol.Key}' specified.");
                }
            }
        }

        private static void VerifyTagUsage(ValidationTemplateInfo template, List<string> results)
        {
            foreach (var tag in template.TemplateTags)
            {
                switch (tag.Key)
                {
                    case "language":
                        VerifyLanguageTagValue(tag, results);
                        break;
                    case "type":
                        VerifyTypeTagValue(tag, results);
                        break;
                    case "wts.type":
                        VerifyWtsTypeTagValue(tag, results);
                        VerifyWtsTypeFeatureMultipleInstancesRule(tag, template, results);
                        break;
                    case "wts.order":
                        VerifyWtsOrderTagValue(tag, results);
                        break;
                    case "wts.displayOrder":
                        VerifyWtsDisplayOrderTagValue(tag, results);
                        break;
                    case "wts.compositionOrder":
                        VerifyWtsCompositionOrderTagValue(tag, results);
                        break;
                    case "wts.framework":
                        VerifyWtsFrameworkTagValue(tag, results);
                        break;
                    case "wts.projecttype":
                        VerifyWtsProjecttypeTagValue(tag, results);
                        break;
                    case "wts.version":
                        VerifyWtsVersionTagValue(tag, results);
                        break;
                    case "wts.genGroup":
                        VerifyWtsGengroupTagValue(tag, results);
                        break;
                    case "wts.rightClickEnabled":
                        VerifyWtsRightclickenabledTagValue(tag, results);
                        break;
                    case "wts.compositionFilter":
                        VerifyWtsCompositionFilterTagValue(tag, results);
                        VerifyWtsCompositionFilterLogic(template, tag, results);
                        break;
                    case "wts.licenses":
                        VerifyWtsLicensesTagValue(tag, results);
                        break;
                    case "wts.group":
                        VerifyWtsGroupTagValue(tag, results);
                        break;
                    case "wts.multipleInstance":
                        VerifyWtsMultipleinstanceTagValue(tag, results);
                        break;
                    case "wts.dependencies":
                        // This value is checked with the TemplateFolderVerifier
                        break;
                    case "wts.defaultInstance":
                        VerifyWtsDefaultinstanceTagValue(tag, results);
                        break;
                    case "wts.export.baseclass":
                        VerifyWtsExportBaseclassTagValue(tag, results);
                        break;
                    case "wts.export.setter":
                        VerifyWtsExportSetterTagValue(tag, results);
                        break;
                    case "wts.isHidden":
                        VerifyWtsIshiddenTagValue(tag, results);
                        break;
                }
            }
        }

        private static void VerifyWtsIshiddenTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (!BoolStrings.Contains(tag.Value))
            {
                results.Add($"Invalid value '{tag.Value}' specified in the wts.isHidden tag.");
            }
        }

        private static void VerifyWtsExportSetterTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (!new[] { "Set" }.Contains(tag.Value))
            {
                results.Add($"Unexpected value '{tag.Value}' specified in the wts.export.setter tag.");
            }
        }

        private static void VerifyWtsExportBaseclassTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (!new[] { "Observable", "ViewModelBase", "INotifyPropertyChanged", "Screen" }.Contains(tag.Value))
            {
                results.Add($"Unexpected value '{tag.Value}' specified in the wts.export.baseclass tag.");
            }
        }

        private static void VerifyWtsDefaultinstanceTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (string.IsNullOrWhiteSpace(tag.Value))
            {
                results.Add("The tag wts.defaultInstance cannot be blank if specified.");
            }
        }

        private static void VerifyWtsMultipleinstanceTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (!BoolStrings.Contains(tag.Value))
            {
                results.Add($"Invalid value '{tag.Value}' specified in the wts.multipleInstance tag.");
            }
        }

        private static void VerifyWtsGroupTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (!new[] { "BackgroundWork", "UserInteraction", "ApplicationLifecycle" }.Contains(tag.Value))
            {
                results.Add($"Invalid value '{tag.Value}' specified in the wts.rightClickEnabled tag.");
            }
        }

        private static void VerifyWtsLicensesTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            // This is a really crude regex designed to catch basic variation from a markdown URI link
            if (!new Regex(@"^\[([\w .\-]){4,}\]\(http([\w ./?=\-:]){9,}\)$").IsMatch(tag.Value))
            {
                results.Add($"'{tag.Value}' specified in the wts.licenses tag does not match the expected format.");
            }
        }

        private static void VerifyWtsCompositionFilterTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            try
            {
                // Use a linked copy of this (and related files) as can't reference the core lib directly
                CompositionQuery.Parse(tag.Value);
            }
            catch (InvalidCompositionQueryException ex)
            {
                results.Add($"Unable to parse the wts.compositionFilter value of '{tag.Value}': {ex}.");
            }
        }

        private static void VerifyWtsCompositionFilterLogic(ValidationTemplateInfo template, KeyValuePair<string, string> tag, List<string> results)
        {
            // Ensure VB tempaltes refere to VB identities
            if (template.TemplateTags["language"] == ProgrammingLanguages.VisualBasic)
            {
                // This can't catch everything but is better than nothing
                if (tag.Value.Contains("identity") && !tag.Value.Contains(".VB"))
                {
                    results.Add($" wts.compositionFilter identitiy vlaue does not match the language. ({tag.Value}).");
                }
            }
        }

        private static void VerifyWtsRightclickenabledTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (!BoolStrings.Contains(tag.Value))
            {
                results.Add($"Invalid value '{tag.Value}' specified in the wts.rightClickEnabled tag.");
            }
        }

        private static void VerifyWtsGengroupTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (!int.TryParse(tag.Value, out int ignoredGetGroupResult))
            {
                results.Add($"The wts.genGroup tag must be an integer. Not '{tag.Value}'.");
            }
        }

        private static void VerifyWtsVersionTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (!new Regex(@"^\d{1,2}.\d{1,2}.\d{1,2}$").IsMatch(tag.Value))
            {
                results.Add(
                    $"'{tag.Value}' specified in the wts.version tag does not match the expected format of 'X.Y.Z'.");
            }
        }

        private static void VerifyWtsProjecttypeTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            // This is only used in the configuration of the project
            // This tag may contain a single value or multiple ones separated by the pipe character
            foreach (var projectType in tag.Value.Split('|'))
            {
                if (!new[] { "Blank", "SplitView", "TabbedPivot" }.Contains(projectType))
                {
                    results.Add($"Invalid value '{tag.Value}' specified in the wts.projecttype tag.");
                }
            }
        }

        private static void VerifyWtsFrameworkTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            // This tag may contain a single value or multiple ones separated by the pipe character
            foreach (var frameworkValue in tag.Value.Split('|'))
            {
                if (!new[] { "MVVMBasic", "MVVMLight", "CodeBehind", "CaliburnMicro" }.Contains(frameworkValue))
                {
                    results.Add($"Invalid value '{tag.Value}' specified in the wts.type tag.");
                }
            }
        }

        private static void VerifyWtsOrderTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            results.Add($"The wts.order tag is no longer supported. Please use the wts.displayOrder or the wts.compositionOrder tag.");
        }

        private static void VerifyWtsDisplayOrderTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (!int.TryParse(tag.Value, out int ignoredOrderResult))
            {
                results.Add($"The wts.displayOrder tag must be an integer. Not '{tag.Value}'.");
            }
        }

        private static void VerifyWtsCompositionOrderTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (!int.TryParse(tag.Value, out int ignoredOrderResult))
            {
                results.Add($"The wts.compositionOrder tag must be an integer. Not '{tag.Value}'.");
            }
        }

        private static void VerifyWtsTypeTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (!new[] { "composition", "page", "feature" }.Contains(tag.Value))
            {
                results.Add($"Invalid value '{tag.Value}' specified in the wts.type tag.");
            }
        }

        private static void VerifyWtsTypeFeatureMultipleInstancesRule(KeyValuePair<string, string> tag, ValidationTemplateInfo template, List<string> results)
        {
            if ("feature".Equals(tag.Value))
            {
                if (template.TemplateTags.Keys.Contains("wts.multipleInstance"))
                {
                    bool.TryParse(template.TemplateTags["wts.multipleInstance"], out var allowMultipleInstances);
                    if (!allowMultipleInstances)
                    {
                        if (string.IsNullOrWhiteSpace(template.TemplateTags["wts.defaultInstance"]))
                        {
                            results.Add($"Template must define a valid value for wts.defaultInstance tag as wts.Type is '{tag.Value}' and wts.multipleInstance is 'false'.");
                        }
                    }
                }
            }
        }

        private static void VerifyTypeTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (!new[] { "item" }.Contains(tag.Value))
            {
                results.Add($"Invalid value '{tag.Value}' specified in the type tag.");
            }
        }

        private static void VerifyLanguageTagValue(KeyValuePair<string, string> tag, List<string> results)
        {
            if (!new[] { ProgrammingLanguages.CSharp, ProgrammingLanguages.VisualBasic }.Contains(tag.Value))
            {
                results.Add($"Invalid value '{tag.Value}' specified in the language tag.");
            }
        }

        private static void EnsureAllDefinedGuidsAreUsed(ValidationTemplateInfo template, string templateRoot, List<string> results)
        {
            if (template.Guids != null)
            {
                var foundGuids = new List<string>();

                foreach (var file in new DirectoryInfo(templateRoot).GetFiles("*.*", SearchOption.AllDirectories))
                {
                    if (file.Name == "template.json")
                    {
                        continue;
                    }

                    var fileText = File.ReadAllText(file.FullName);

                    foreach (var guid in template.Guids)
                    {
                        if (fileText.Contains(guid))
                        {
                            foundGuids.Add(guid);
                        }
                    }
                }

                foreach (var templateGuid in template.Guids)
                {
                    if (!foundGuids.Contains(templateGuid))
                    {
                        results.Add($"Defined GUID '{templateGuid}' is not used.");
                    }
                }
            }
        }

        private static void EnsureAllDefinedPrimaryOutputsExist(ValidationTemplateInfo template, string templateRoot, List<string> results)
        {
            if (template.PrimaryOutputs != null)
            {
                foreach (var primaryOutput in template.PrimaryOutputs)
                {
                    if (!File.Exists(Path.Combine(templateRoot, primaryOutput.Path)))
                    {
                        results.Add($"Primary output '{primaryOutput.Path}' does not exist.");
                    }
                }
            }
        }

        private static void EnsureClassificationAsExpected(ValidationTemplateInfo template, List<string> results)
        {
            if (template.Classifications.Count != 1)
            {
                results.Add("Only a single classification is exected.");
            }
            else if (template.Classifications.First() != "Universal")
            {
                results.Add("Classification of 'Universal' is exected.");
            }
        }

        private static void EnsureAdequateDescription(ValidationTemplateInfo template, List<string> results)
        {
            if (string.IsNullOrWhiteSpace(template.Description))
            {
                results.Add("Description not provided.");
            }
            else if (template.Description.Trim().Length < 15)
            {
                results.Add("Description is too short.");
            }
        }

        private static void EnsureVisualBasicTemplatesAreIdentifiedAppropriately(ValidationTemplateInfo template, string filePath, List<string> results)
        {
            var isVbTemplate = filePath.Contains("VB\\");

            if (string.IsNullOrWhiteSpace(template.Identity))
            {
                results.Add("The template is missing an identity.");
            }
            else
            {
                if (isVbTemplate)
                {
                    if (!template.Identity.EndsWith("VB", StringComparison.CurrentCulture))
                    {
                        results.Add("The identity of templates for VisualBasic should end with 'VB'.");
                    }
                }
                else
                {
                    if (template.Identity.EndsWith("VB", StringComparison.CurrentCulture))
                    {
                        results.Add("Only VisualBasic templates should end with 'VB'.");
                    }
                }
            }
        }
    }
}
