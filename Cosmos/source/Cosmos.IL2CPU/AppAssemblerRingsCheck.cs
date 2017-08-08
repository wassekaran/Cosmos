using System;
using System.Linq;
using System.Reflection;

using Cosmos.Assembler;
using Cosmos.IL2CPU.API;
using Cosmos.IL2CPU.API.Attribs;

namespace Cosmos.IL2CPU {
    public static class AppAssemblerRingsCheck {
        private static bool IsAssemblySkippedDuringRingCheck(Assembly assembly) {
            // Disable all rings for now. We will reenable them in Gen3 and then in an abstracted way.
            // No real need to keep them enabled now for G2 which will be deprecated soon anyway.
            return true;

            var xServicable = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Where(x => x.Key == "Serviceable").SingleOrDefault();
            var xNetFrameworkAssembly = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Where(x => x.Key == ".NETFrameworkAssembly").SingleOrDefault();
            var xProduct = assembly.GetCustomAttribute<AssemblyProductAttribute>();
            var xCopyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
            var xCompany = assembly.GetCustomAttribute<AssemblyCompanyAttribute>();
            string xName = assembly.GetName().Name;

            if ((xServicable?.Value == "True" || xNetFrameworkAssembly != null) &&
                ((xProduct != null && xProduct.Product.Contains(".NET Framework"))
                || (xCopyright != null && xCopyright.Copyright.Contains("Microsoft Corporation"))
                || (xCompany != null && xCompany.Company.Contains("Microsoft Corporation")))) {
                return true;
            }

            // Assemblies with no rings
            if ((xName == "Cosmos.Debug.Kernel") ||
                (xName == "Cosmos.Debug.Kernel.Plugs.Asm") ||
                (xName == "Cosmos.IL2CPU") ||
                (xName == "Cosmos.IL2CPU.API") ||
                (xName == "Cosmos.Common")) {
                return true;
            }

            return false;
        }

        /// <summary>
        /// This method validates rings. Assemblies are specific for a given ring and are able to be dependent on assemblies in
        /// the same ring or one ring "up" (ie, User can reference System, etc), but not other way around.
        /// </summary>
        /// <param name="scanner"></param>
        /// <param name="entryAssembly"></param>
        public static void Execute(ILScanner scanner, Assembly entryAssembly) {
            if (entryAssembly == null) {
                throw new ArgumentNullException(nameof(entryAssembly));
            }

            RingsWriteLine("Start check");

            // verify the entry assembly is in the User ring.
            var xRing = GetRingFromAssembly(entryAssembly);
            if (xRing != RingAttribute.RingEnum.User) {
                throw new Exception($"Assembly '{entryAssembly.GetName().Name}' contains your kernel class, which means it should be in the ring {RingAttribute.RingEnum.User}!");
            }

            foreach (var xAssembly in scanner.mUsedAssemblies) {
                if (IsAssemblySkippedDuringRingCheck(xAssembly)) {
                    continue;
                }

                RingsWriteLine("Assembly '{0}'", xAssembly.GetName().Name);
                xRing = GetRingFromAssembly(xAssembly);
                var xRingInt = (int)xRing;

                RingsWriteLine("\t\tRing = {0}", xRing);
                foreach (var xAsmDepRef in xAssembly.GetReferencedAssemblies()) {
                    var xAsmDep = scanner.mUsedAssemblies.FirstOrDefault(i => i.GetName().Name == xAsmDepRef.Name);
                    if (xAsmDep == null || IsAssemblySkippedDuringRingCheck(xAsmDep)) {
                        continue;
                    }
                    RingsWriteLine("\tDependency '{0}'", xAsmDepRef.Name);
                    var xDepRing = GetRingFromAssembly(xAsmDep);
                    RingsWriteLine("\t\tRing = {0}", xDepRing);

                    var xDepRingInt = (int)xDepRing;

                    if (xDepRingInt == xRingInt) {
                        // assembly and its dependency are in the same ring.
                        continue;
                    }
                    if (xDepRingInt > xRingInt) {
                        throw new Exception($"Assembly '{xAssembly.GetName().Name}' is in ring {xRing}({xRingInt}). It references assembly '{xAsmDepRef.Name}' which is in ring {xDepRing}({xDepRingInt}), but this is not allowed!");
                    }

                    var xRingDiff = xRingInt - xDepRingInt;
                    if (xRingDiff == 1) {
                        // 1 level up is allowed
                        continue;
                    }
                    throw new Exception($"Assembly '{xAssembly.GetName().Name}' is in ring {xRing}({xRingInt}). It references assembly '{xAsmDepRef.Name}' which is in ring {xDepRing}({xDepRingInt}), but this is not allowed!");
                }

                // now do per-ring checks:
                switch (xRing) {
                    case RingAttribute.RingEnum.User:
                        ValidateUserAssembly(xAssembly);
                        break;
                    case RingAttribute.RingEnum.Core:
                        ValidateCoreAssembly(xAssembly);
                        break;
                    case RingAttribute.RingEnum.HAL:
                        ValidateHALAssembly(xAssembly);
                        break;
                    case RingAttribute.RingEnum.System:
                        ValidateSystemAssembly(xAssembly);
                        break;
                    default:
                        throw new NotImplementedException($"Ring {xRing} not implemented");
                }
            }
        }

        private static bool HasAssemblyPlugs(Assembly assembly) {
            foreach (var xType in assembly.GetTypes()) {
                if (xType.GetTypeInfo().IsSubclassOf(typeof(AssemblerMethod))) {
                    return true;
                }
            }
            return false;
        }

        private static void ValidateCoreAssembly(Assembly assembly) {
            // any checks to do?
        }

        private static void ValidateHALAssembly(Assembly assembly) {
            if (HasAssemblyPlugs(assembly)) {
                throw new Exception($"HAL assembly '{assembly.GetName().Name}' uses Assembly plugs, which are not allowed!");
            }
        }

        private static void ValidateSystemAssembly(Assembly assembly) {
            if (HasAssemblyPlugs(assembly)) {
                throw new Exception(String.Format("System assembly '{0}' uses Assembly plugs, which are not allowed!", assembly.GetName().Name));
            }
        }

        private static void ValidateUserAssembly(Assembly assembly) {
            if (HasAssemblyPlugs(assembly)) {
                throw new Exception($"User assembly '{assembly.GetName().Name}' uses Assembly plugs, which are not allowed!");
            }
        }

        private static RingAttribute.RingEnum GetRingFromAssembly(Assembly assembly) {
            var xRingAttrib = assembly.GetCustomAttribute<RingAttribute>();

            if (xRingAttrib == null) {
                return RingAttribute.RingEnum.User;
            }

            return xRingAttrib.Ring;
        }

        private static void RingsWriteLine(string line, params object[] args) {
            Console.WriteLine("Rings: " + String.Format(line.Replace("\t", "    "), args));
        }
    }
}