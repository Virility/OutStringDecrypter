using System;               
using System.Reflection;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using OutStringDecrypter.Enums;

namespace OutStringDecrypter.Helpers
{
    public class Deobfuscator
    {
        public ModuleDefMD Module { get; set; }

        public string FilePath { get; set; }

        public Deobfuscator(ModuleDefMD module, string filePath)
        {
            if (module == null)
                throw new ArgumentNullException("module");

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException("filePath");

            FilePath = filePath;
            Module = module;
        }

        public bool Process(string filePath)
        {
            try
            {
                Console.WriteLine("Cleaning strings..");
                Clean(CleanType.String);

                Module.Write(filePath);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occured: {0}", ex.Message);
                Console.WriteLine(ex.StackTrace);
            }

            return false;
        }

        private void Clean(CleanType cleanType)
        {
            switch (cleanType)
            {
                case CleanType.String:
                    var decryptionMethod = FindStringDecryptionMethod();
                    if (decryptionMethod == null)
                        return;

                    var declaringType = decryptionMethod.DeclaringType;

                    var cleanedCount = DecryptAllStrings(decryptionMethod, declaringType);
                    Console.WriteLine("Successfully decrypted {0} strings..", cleanedCount);

                    break;
            }
        }

        private MethodDef FindStringDecryptionMethod()
        {
            foreach (var type in Module.Types)
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody || !method.Body.HasInstructions)
                        continue;

                    var instructions = method.Body.Instructions;
                    if (instructions.Count >= 30)
                        continue;

                    var hasLibraryString = false;
                    var hasDecryptionString = false;

                    foreach (var instruction in instructions)
                    {
                        if (instruction.OpCode.Code != Code.Ldstr)
                            continue;

                        var operandValue = instruction.Operand.ToString();
                        if (!hasLibraryString)
                            hasLibraryString = operandValue.Equals("Protect.NET_Lib.Encryption");
                        else
                            hasDecryptionString = operandValue.Equals("Decrypt");

                        if (hasLibraryString && hasDecryptionString)
                            return method;
                    }
                }
            }

            return null;
        }

        private int DecryptAllStrings(IMDTokenProvider decryptionMethod, IFullName declaringType)
        {
            var assembly = Assembly.LoadFile(FilePath);
            var decryptCount = 0;

            foreach (var type in Module.Types)
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody || !method.Body.HasInstructions)
                        continue;

                    var instructions = method.Body.Instructions;

                    for (var i = 0; i < instructions.Count; i++)
                    {
                        if (instructions[i].OpCode != OpCodes.Ldstr)
                            continue;

                        if (instructions[i + 1].OpCode != OpCodes.Ldstr)
                            continue;

                        if (!instructions[i + 2].Operand.ToString().
                            Equals(decryptionMethod.ToString()))
                            continue;

                        var param1 = instructions[i].Operand.ToString();
                        var param2 = instructions[i + 1].Operand.ToString();             

                        var methodType = assembly.GetType(declaringType.Name);
                        if (methodType == null)
                            continue;    

                        var metaData = decryptionMethod.MDToken.ToInt32(); 
                        var methodBase = methodType.Module.ResolveMethod(metaData);
                        if (methodBase == null)
                            continue;

                        var parameters = methodBase.GetParameters();
                        if (parameters.Length == 0)
                            continue;

                        var result
                            = methodBase.Invoke(null, new object[] { param1, param2 });

                        var body = method.Body;

                        body.Instructions[i].OpCode = OpCodes.Ldstr;
                        body.Instructions[i].Operand = result.ToString();

                        body.Instructions.RemoveAt(i + 1);
                        body.Instructions.RemoveAt(i + 1);

                        decryptCount++;
                    }
                }
            }

            return decryptCount;
        }
    }
}
