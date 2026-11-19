using System.Linq;
using FluidHTN;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Fluid_HTN.UnitTests
{
    /// <summary>
    /// Fork fix 3 (Dauie/numun#283): the library writes nothing to a console.
    /// Console and ConsoleColor are in the System.Console assembly. A library that uses
    /// either type references that assembly, so the test reads the assembly references.
    /// </summary>
    [TestClass]
    public class LibraryReferenceTests
    {
        [TestMethod]
        public void LibraryDoesNotReferenceSystemConsole()
        {
            var library = typeof(Domain<>).Assembly;

            var references = library.GetReferencedAssemblies().Select(name => name.Name).ToList();

            CollectionAssert.DoesNotContain(references, "System.Console");
        }
    }
}
