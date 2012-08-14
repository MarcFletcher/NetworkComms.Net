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

        private Dictionary<Type, ISerialize> SerializersByType = new Dictionary<Type, ISerialize>();
        private Dictionary<byte, ISerialize> SerializersByID = new Dictionary<byte, ISerialize>();
        private Dictionary<Type, ICompress> CompressorsByType = new Dictionary<Type, ICompress>();
        private Dictionary<byte, ICompress> CompressorsByID = new Dictionary<byte, ICompress>();

        static WrappersHelper instance;
        
        public static WrappersHelper Instance { get { return instance; } }

        static WrappersHelper()
        {
            instance = new WrappersHelper();
        }

        public Dictionary<Type, ISerialize> GetAllSerializes()
        {
            return SerializersByType;
        }

        public ISerialize GetSerializer<T>() where T : ISerialize
        {
            if (SerializersByType.ContainsKey(typeof(T)))
                return SerializersByType[typeof(T)];
            else
                return null;
        }

        public ISerialize GetSerializer(byte Id)
        {
            if (SerializersByID.ContainsKey(Id))
                return SerializersByID[Id];
            else
                return null;
        }

        public Dictionary<Type, ICompress> GetAllCompressors()
        {            
            return CompressorsByType;
        }

        public ICompress GetCompressor<T>() where T : ICompress
        {
            if (CompressorsByType.ContainsKey(typeof(T)))
                return CompressorsByType[typeof(T)];
            else
                return null;
        }

        public ICompress GetCompressor(byte Id)
        {
            if (CompressorsByID.ContainsKey(Id))
                return CompressorsByID[Id];
            else
                return null;
        }

        public void AddCompressor(ICompress instance)
        {
            if (CompressorsByType.ContainsKey(instance.GetType()))
                if (CompressorsByType[instance.GetType()] != instance)
                    throw new Exception();
                else
                    return;

            CompressorsByType.Add(instance.GetType(), instance);
            CompressorsByID.Add(instance.Identifier, instance);
        }

        public void AddSerializer(ISerialize instance)
        {
            if (SerializersByType.ContainsKey(instance.GetType()))
                if (SerializersByType[instance.GetType()] != instance)
                    throw new Exception();
                else
                    return;

            SerializersByType.Add(instance.GetType(), instance);
            SerializersByID.Add(instance.Identifier, instance);
        }

        private WrappersHelper()
        {
            //An aggregate catalog that combines multiple catalogs
            var catalog = new AggregateCatalog();

            //We're going to try and guess where we might have serializers and compressors.  We will look in all currently loaded assemblies
            //and all dll and exe files in the application root directory and subdirectories

            //Add all the currently loaded assemblies
            AppDomain.CurrentDomain.GetAssemblies().ToList().ForEach(ass => catalog.Catalogs.Add(new AssemblyCatalog(ass)));

            var allDirectories = Directory.GetDirectories(Directory.GetCurrentDirectory(), "*", SearchOption.AllDirectories);

            //Adds all the parts found in dlls in the same localtion
            catalog.Catalogs.Add(new DirectoryCatalog(Directory.GetCurrentDirectory(), "*.dll"));
            //Adds all the parts found in exe in the same localtion
            catalog.Catalogs.Add(new DirectoryCatalog(Directory.GetCurrentDirectory(), "*.exe"));

            for (int i = 0; i < allDirectories.Length; i++)
            {
                if (Directory.GetFiles(allDirectories[i], "*.exe", SearchOption.TopDirectoryOnly).Union(
                    Directory.GetFiles(allDirectories[i], "*.dll", SearchOption.TopDirectoryOnly)).Count() != 0)
                {
                    //Adds all the parts found in dlls in the same localtion
                    catalog.Catalogs.Add(new DirectoryCatalog(allDirectories[i], "*.dll"));
                    //Adds all the parts found in exe in the same localtion
                    catalog.Catalogs.Add(new DirectoryCatalog(allDirectories[i], "*.exe"));
                }
            }

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

            SerializersByType = serializers.Distinct(SerializerComparer.Instance).ToDictionary(l => l.GetType(), l => l);
            CompressorsByType = compressors.Distinct(CompressorComparer.Instance).ToDictionary(l => l.GetType(), l => l);

            SerializersByID = SerializersByType.ToDictionary(s => s.Value.Identifier, s => s.Value);
            CompressorsByID = CompressorsByType.ToDictionary(c => c.Value.Identifier, c => c.Value);
        }
    }
}
