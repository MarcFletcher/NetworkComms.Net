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
    public sealed class ProcessorManager
    {
        class SerializerComparer : IEqualityComparer<Serializer>
        {
            public static SerializerComparer Instance { get; private set; }

            static SerializerComparer() { Instance = new SerializerComparer(); }

            public SerializerComparer(){}

            #region IEqualityComparer<ISerialize> Members

            public bool Equals(Serializer x, Serializer y)
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

            public int GetHashCode(Serializer obj)
            {
                return obj.Identifier.GetHashCode() ^ obj.GetType().GetHashCode();
            }

            #endregion
        }

        class DataProcessorComparer : IEqualityComparer<DataProcessor>
        {
            public static DataProcessorComparer Instance { get; private set; }

            static DataProcessorComparer() { Instance = new DataProcessorComparer(); }

            public DataProcessorComparer() { }

            #region IEqualityComparer<ICompress> Members

            public bool Equals(DataProcessor x, DataProcessor y)
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

            public int GetHashCode(DataProcessor obj)
            {
                return obj.Identifier.GetHashCode() ^ obj.GetType().GetHashCode();
            }

            #endregion
        }

        private CompositionContainer _container;

        [ImportMany(typeof(Serializer))]
        private IEnumerable<Serializer> serializers = null;

        [ImportMany(typeof(DataProcessor))]
        private IEnumerable<DataProcessor> compressors = null;

        private Dictionary<Type, Serializer> SerializersByType = new Dictionary<Type, Serializer>();
        private Dictionary<byte, Serializer> SerializersByID = new Dictionary<byte, Serializer>();
        private Dictionary<Type, DataProcessor> DataProcessorsByType = new Dictionary<Type, DataProcessor>();
        private Dictionary<byte, DataProcessor> DataProcessorsByID = new Dictionary<byte, DataProcessor>();

        static ProcessorManager instance;
        
        public static ProcessorManager Instance { get { return instance; } }

        static ProcessorManager()
        {
            instance = new ProcessorManager();
        }

        public Dictionary<Type, Serializer> GetAllSerializes()
        {
            return SerializersByType;
        }

        public Serializer GetSerializer<T>() where T : Serializer
        {
            if (SerializersByType.ContainsKey(typeof(T)))
                return SerializersByType[typeof(T)];
            else
                return null;
        }

        public Serializer GetSerializer(byte Id)
        {
            if (SerializersByID.ContainsKey(Id))
                return SerializersByID[Id];
            else
                return null;
        }

        public Dictionary<Type, DataProcessor> GetAllDataProcessors()
        {            
            return DataProcessorsByType;
        }

        public DataProcessor GetDataProcessor<T>() where T : DataProcessor
        {
            if (DataProcessorsByType.ContainsKey(typeof(T)))
                return DataProcessorsByType[typeof(T)];
            else
                return null;
        }

        public DataProcessor GetDataProcessor(byte Id)
        {
            if (DataProcessorsByID.ContainsKey(Id))
                return DataProcessorsByID[Id];
            else
                return null;
        }

        public void AddDataProcessor(DataProcessor instance)
        {
            if (DataProcessorsByType.ContainsKey(instance.GetType()))
                if (DataProcessorsByType[instance.GetType()] != instance)
                    throw new Exception();
                else
                    return;

            DataProcessorsByType.Add(instance.GetType(), instance);
            DataProcessorsByID.Add(instance.Identifier, instance);
        }

        public void AddSerializer(Serializer instance)
        {
            if (SerializersByType.ContainsKey(instance.GetType()))
                if (SerializersByType[instance.GetType()] != instance)
                    throw new Exception();
                else
                    return;
            
            SerializersByType.Add(instance.GetType(), instance);
            SerializersByID.Add(instance.Identifier, instance);
        }

        public long CreateSerializerDataProcessorIdentifier(Serializer serializer, List<DataProcessor> dataProcessors)
        {
            long res = 0;
                        
            res |= serializer.Identifier;
            res <<= 8;

            if (dataProcessors != null && dataProcessors.Count != 0)
            {
                if (dataProcessors.Count > 7)
                    throw new InvalidOperationException("Cannot specify more than 7 data processors for automatic serialization detection");

                for (int i = 0; i < dataProcessors.Count; i++)
                {
                    res |= dataProcessors[i].Identifier;

                    if (i != dataProcessors.Count - 1)
                        res <<= 8;
                }

                if (dataProcessors.Count < sizeof(long) - 1)
                    res <<= (8 * (sizeof(long) - 1 - dataProcessors.Count));
            }

            return res;
        }

        public void GetSerializerDataProcessorsFromIdentifier(long id, out Serializer serializer, out List<DataProcessor> dataProcessors)
        {
            serializer = GetSerializer((byte)(id >> 56));

            dataProcessors = new List<DataProcessor>();

            for (int i = 6; i >= 0; i--)
            {
                long mask = 0xFF;
                byte processorId = (byte)((id & (mask << (8 * i))) >> (8 * i));

                if (processorId != 0)
                    dataProcessors.Add(GetDataProcessor(processorId));
            }
        }

        private ProcessorManager()
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

            var serializersToAdd = serializers.Distinct(SerializerComparer.Instance).Where(s => !SerializersByType.ContainsKey(s.GetType())).ToArray();
            var dataProcessorsToAdd = compressors.Distinct(DataProcessorComparer.Instance).Where(c => !DataProcessorsByType.ContainsKey(c.GetType())).ToArray();

            foreach (var instance in serializersToAdd)
            {
                SerializersByType.Add(instance.GetType(), instance);
                SerializersByID.Add(instance.Identifier, instance);
            }

            foreach (var instance in dataProcessorsToAdd)
            {
                DataProcessorsByType.Add(instance.GetType(), instance);
                DataProcessorsByID.Add(instance.Identifier, instance);
            }                        
        }
    }
}
