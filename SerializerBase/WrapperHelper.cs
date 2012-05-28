using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition.Hosting;
using SerializerBase;
using System.ComponentModel.Composition;
using System.IO;

namespace SerializerBase
{
    public sealed class WrappersHelper
    {
        class SerializerComparer : IEqualityComparer<ISerialize>
        {
            public static SerializerComparer Instance { get; private set; }

            static SerializerComparer() { Instance = new SerializerComparer(); }

            public SerializerComparer(){}

            #region IEqualityComparer<ISerialize> Members

            public bool Equals(ISerialize x, ISerialize y)
            {
                if (x.Identifier == y.Identifier)
                {
                    Type xType = x.GetType();
                    Type yType = y.GetType();

                    if (xType != yType)
                        throw new InvalidOperationException("Cannot have two different serializer types with same identifier");
                    else
                        return true;
                }
                else
                    return false;
            }

            public int GetHashCode(ISerialize obj)
            {
                return obj.Identifier.GetHashCode() ^ obj.GetType().GetHashCode();
            }

            #endregion
        }

        class CompressorComparer : IEqualityComparer<ICompress>
        {
            public static CompressorComparer Instance { get; private set; }

            static CompressorComparer() { Instance = new CompressorComparer(); }

            public CompressorComparer() { }

            #region IEqualityComparer<ICompress> Members

            public bool Equals(ICompress x, ICompress y)
            {
                if (x.Identifier == y.Identifier)
                {
                    Type xType = x.GetType();
                    Type yType = y.GetType();

                    if (xType != yType)
                        throw new InvalidOperationException("Cannot have two different serializer types with same identifier");
                    else
                        return true;
                }
                else
                    return false;
            }

            public int GetHashCode(ICompress obj)
            {
                return obj.Identifier.GetHashCode() ^ obj.GetType().GetHashCode();
            }

            #endregion
        }

        private CompositionContainer _container;

        [ImportMany(typeof(ISerialize))]
        private IEnumerable<ISerialize> serializers = null;

        [ImportMany(typeof(ICompress))]
        private IEnumerable<ICompress> compressors = null;

        private Dictionary<Type, ISerialize> Serializers = new Dictionary<Type, ISerialize>();
        private Dictionary<Type, ICompress> Compressors = new Dictionary<Type, ICompress>();

        static WrappersHelper instance;
        
        public static WrappersHelper Instance { get { return instance; } }

        static WrappersHelper()
        {
            instance = new WrappersHelper();
        }

        public Dictionary<Type, ISerialize> GetAllSerializes()
        {
            return Serializers;
        }

        public ISerialize GetSerializer<T>() where T : ISerialize
        {
            return Serializers[typeof(T)];
        }

        public Dictionary<Type, ICompress> GetAllCompressors()
        {
            return Compressors;
        }

        public ICompress GetCompressor<T>() where T : ICompress
        {
            return Compressors[typeof(T)];
        }

        private WrappersHelper()
        {            
            //An aggregate catalog that combines multiple catalogs
            var catalog = new AggregateCatalog();

            //Add all the currently loaded assemblies
            AppDomain.CurrentDomain.GetAssemblies().ToList().ForEach(ass => catalog.Catalogs.Add(new AssemblyCatalog(ass)));
            //Adds all the parts found in dlls in the same localtion
            catalog.Catalogs.Add(new DirectoryCatalog(".//", "*.dll"));
            //Adds all the parts found in exe in the same localtion
            catalog.Catalogs.Add(new DirectoryCatalog(".//", "*.exe"));

            _container = new CompositionContainer(catalog);
                        
            //Fill the imports of this object
            try
            {
                _container.ComposeParts(this);
            }
            catch (CompositionException compositionException)
            {
                Console.WriteLine(compositionException.ToString());
            }           

            Serializers = serializers.Distinct(SerializerComparer.Instance).ToDictionary(l => l.GetType(), l => l);
            Compressors = compressors.Distinct(CompressorComparer.Instance).ToDictionary(l => l.GetType(), l => l);
        }
    }
}
