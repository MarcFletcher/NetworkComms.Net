//  Copyright 2011 Marc Fletcher, Matthew Dean
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition.Hosting;
using DPSBase;
using System.ComponentModel.Composition;
using System.IO;
using System.Reflection;


namespace DPSBase
{
    /// <summary>
    /// Automatically detects and manages the use of <see cref="DataSerializer"/> and <see cref="DataProcessor"/>s.  Any <see cref="DataSerializer"/> or <see cref="DataProcessor"/> in an assembly located in the working directory (including subdirectories) will be automatically detected
    /// </summary>
    public sealed class DPSManager
    {
        #region Comparers

        class SerializerComparer : IEqualityComparer<DataSerializer>
        {
            public static SerializerComparer Instance { get; private set; }

            static SerializerComparer() { Instance = new SerializerComparer(); }

            public SerializerComparer() { }

            #region IEqualityComparer<ISerialize> Members

            public bool Equals(DataSerializer x, DataSerializer y)
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

            public int GetHashCode(DataSerializer obj)
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

        class AssemblyComparer : IEqualityComparer<AssemblyName>
        {
            public static AssemblyComparer Instance { get; private set; }

            static AssemblyComparer() { Instance = new AssemblyComparer(); }

            public AssemblyComparer() { }

            #region IEqualityComparer<AssemblyName> Members

            public bool Equals(AssemblyName x, AssemblyName y)
            {
                return x.FullName == y.FullName;               
            }

            public int GetHashCode(AssemblyName obj)
            {
                return obj.FullName.GetHashCode();
            }

            #endregion
        }

        #endregion

        private CompositionContainer _container;

        [ImportMany(typeof(DataSerializer))]
        private IEnumerable<DataSerializer> serializers = null;

        [ImportMany(typeof(DataProcessor))]
        private IEnumerable<DataProcessor> compressors = null;

        private Dictionary<Type, DataSerializer> SerializersByType = new Dictionary<Type, DataSerializer>();
        private Dictionary<byte, DataSerializer> SerializersByID = new Dictionary<byte, DataSerializer>();
        private Dictionary<Type, DataProcessor> DataProcessorsByType = new Dictionary<Type, DataProcessor>();
        private Dictionary<byte, DataProcessor> DataProcessorsByID = new Dictionary<byte, DataProcessor>();

        static DPSManager instance;

        static DPSManager()
        {
            instance = new DPSManager();
        }

        /// <summary>
        /// Retrieves all <see cref="DataSerializer"/>s known to the DPSManager
        /// </summary>
        /// <returns>A dictionary whose key is the <see cref="System.Type"/> of the <see cref="DataSerializer"/> instance that is the value</returns>
        public static Dictionary<Type, DataSerializer> GetAllDataSerializes()
        {
            return instance.SerializersByType;
        }

        /// <summary>
        /// Retrieves the singleton instance of the <see cref="DataSerializer"/> with <see cref="System.Type"/> T
        /// </summary>
        /// <typeparam name="T">The <see cref="System.Type"/> of the <see cref="DataSerializer"/> to retrieve </typeparam>
        /// <returns>The retrieved singleton instance of the desired <see cref="DataSerializer"/></returns>
        public static DataSerializer GetDataSerializer<T>() where T : DataSerializer
        {
            if (instance.SerializersByType.ContainsKey(typeof(T)))
                return instance.SerializersByType[typeof(T)];
            else
                return null;
        }

        /// <summary>
        /// Retrieves the singleton instance of the <see cref="DataSerializer"/> corresponding to a given id
        /// </summary>
        /// <param name="Id">The identifier corresponding to the desired <see cref="DataSerializer"/></param>
        /// <returns>The retrieved singleton instance of the desired <see cref="DataSerializer"/></returns>
        public static DataSerializer GetDataSerializer(byte Id)
        {
            if (instance.SerializersByID.ContainsKey(Id))
                return instance.SerializersByID[Id];
            else
                return null;
        }

        /// <summary>
        /// Retrieves all <see cref="DataProcessor"/>s known to the DPSManager
        /// </summary>
        /// <returns>A dictionary whose key is the <see cref="System.Type"/> of the <see cref="DataProcessor"/> instance that is the value</returns>
        public static Dictionary<Type, DataProcessor> GetAllDataProcessors()
        {
            return instance.DataProcessorsByType;
        }

        /// <summary>
        /// Retrieves the singleton instance of the <see cref="DataProcessor"/> with <see cref="System.Type"/> T
        /// </summary>
        /// <typeparam name="T">The <see cref="System.Type"/> of the <see cref="DataProcessor"/> to retrieve </typeparam>
        /// <returns>The retrieved singleton instance of the desired <see cref="DataProcessor"/></returns>
        public static DataProcessor GetDataProcessor<T>() where T : DataProcessor
        {
            if (instance.DataProcessorsByType.ContainsKey(typeof(T)))
                return instance.DataProcessorsByType[typeof(T)];
            else
                return null;
        }

        /// <summary>
        /// Retrieves the singleton instance of the <see cref="DataProcessor"/> corresponding to a given id
        /// </summary>
        /// <param name="Id">The identifier corresponding to the desired <see cref="DataProcessor"/></param>
        /// <returns>The retrieved singleton instance of the desired <see cref="DataProcessor"/></returns>
        public static DataProcessor GetDataProcessor(byte Id)
        {
            if (instance.DataProcessorsByID.ContainsKey(Id))
                return instance.DataProcessorsByID[Id];
            else
                return null;
        }

        /// <summary>
        /// Allows the addition of <see cref="DataProcessor"/>s which are not autodetected.  Use only if the assmbley in which the <see cref="DataProcessor"/> is defined is not in the working directory (including subfolders) or if automatic detection is not supported on your platform
        /// </summary>
        /// <param name="dataProcessor">The <see cref="DataProcessor"/> to make the see <see cref="DPSManager"/> aware of</param>
        /// <exception cref="ArgumentException">Thrown if A different <see cref="DataProcessor"/> of the same <see cref="System.Type"/> or Id has already been added to the <see cref="DPSManager"/></exception>
        public static void AddDataProcessor(DataProcessor dataProcessor)
        {
            if (instance.DataProcessorsByType.ContainsKey(dataProcessor.GetType()))
                if (instance.DataProcessorsByType[dataProcessor.GetType()] != dataProcessor)
                    throw new ArgumentException("A different DataProcessor of the same Type or Id has already been added to DPSManager");
                else
                    return;

            instance.DataProcessorsByType.Add(dataProcessor.GetType(), dataProcessor);
            instance.DataProcessorsByID.Add(dataProcessor.Identifier, dataProcessor);
        }

        /// <summary>
        /// Allows the addition of <see cref="DataSerializer"/>s which are not autodetected.  Use only if the assmbley in which the <see cref="DataSerializer"/> is defined is not in the working directory (including subfolders) or if automatic detection is not supported on your platform
        /// </summary>
        /// <param name="dataSerializer">The <see cref="DataSerializer"/> to make the see <see cref="DPSManager"/> aware of</param>
        /// <exception cref="ArgumentException">Thrown if A different <see cref="DataSerializer"/> of the same <see cref="System.Type"/> or Id has already been added to the <see cref="DPSManager"/></exception>
        public static void AddDataSerializer(DataSerializer dataSerializer)
        {
            if (instance.SerializersByType.ContainsKey(dataSerializer.GetType()))
                if (instance.SerializersByType[dataSerializer.GetType()] != dataSerializer)
                    throw new ArgumentException("A different DataSerializer of the same Type or Id has already been added to DPSManager");
                else
                    return;

            instance.SerializersByType.Add(dataSerializer.GetType(), dataSerializer);
            instance.SerializersByID.Add(dataSerializer.Identifier, dataSerializer);
        }

        /// <summary>
        /// Generates an <see cref="long"/> describing a <see cref="DataSerializer"/> and a set of <see cref="DataProcessor"/>s
        /// </summary>
        /// <param name="serializer">The <see cref="DataSerializer"/> to be used</param>
        /// <param name="dataProcessors">A <see cref="System.Collections.Generic.List{DataProcessor}()"/> to be used.  The order of this </param>
        /// <returns>A <see cref="long"/> describing the arguments</returns>
        /// <exception cref="ArgumentException">Thrown is more than 7 <see cref="DataSerializer"/>s are used</exception>
        /// <remarks>This method is used to specify succinctly the serialization method and any data processing that will be used when transmitting data using NetworkCommsDotNet</remarks>
        public static long CreateSerializerDataProcessorIdentifier(DataSerializer serializer, List<DataProcessor> dataProcessors)
        {
            long res = 0;

            res |= serializer.Identifier;
            res <<= 8;

            if (dataProcessors != null && dataProcessors.Count != 0)
            {
                if (dataProcessors.Count > 7)
                    throw new ArgumentException("Cannot specify more than 7 data processors for automatic serialization detection");

                for (int i = 0; i < dataProcessors.Count; i++)
                {
                    res |= dataProcessors[i].Identifier;

                    if (i != dataProcessors.Count - 1)
                        res <<= 8;
                }

                if (dataProcessors.Count < sizeof(long) - 1)
                    res <<= (8 * (sizeof(long) - 1 - dataProcessors.Count));
            }
            else
                res <<= (8 * (sizeof(long) - 2));

            return res;
        }

        /// <summary>
        /// Takes an identifier generated using <see cref="DPSManager.CreateSerializerDataProcessorIdentifier"/> and returns the <see cref="DataSerializer"/> and set of <see cref="DataProcessor"/>s used to generate the identifier
        /// </summary>
        /// <param name="id">The <see cref="long"/> describing the <see cref="DataSerializer"/> and a set of <see cref="DataProcessor"/>s</param>
        /// <param name="serializer">The resultant <see cref="DataSerializer"/></param>
        /// <param name="dataProcessors">A List of the resultant <see cref="DataProcessor"/>s</param>
        /// <remarks>This method is used to extract the serialization method and any data processing that needs to be used when transmitting data using NetworkCommsDotNet</remarks>
        public static void GetSerializerDataProcessorsFromIdentifier(long id, out DataSerializer serializer, out List<DataProcessor> dataProcessors)
        {
            serializer = GetDataSerializer((byte)(id >> 56));

            dataProcessors = new List<DataProcessor>();

            for (int i = 6; i >= 0; i--)
            {
                long mask = 0xFF;
                byte processorId = (byte)((id & (mask << (8 * i))) >> (8 * i));

                if (processorId != 0)
                    dataProcessors.Add(GetDataProcessor(processorId));
            }
        }

        private DPSManager()
        {
            try
            {
                //An aggregate catalog that combines multiple catalogs
                var catalog = new AggregateCatalog();

                //We're now going to look through the assemly reference tree to look for more components
                //This will be done by first checking whether a relefection only load of each assembly and checking 
                //for reference to DPSBase.  We will therefore get the full name of DPSBase
                var dpsBaseAssembly = typeof(DPSManager).Assembly.GetName();

                catalog.Catalogs.Add(new AssemblyCatalog(typeof(DPSManager).Assembly));

                //First add all currently loaded assemblies
                AppDomain.CurrentDomain.GetAssemblies().ToList().ForEach(ass =>  {
                    if (ass.GetReferencedAssemblies().Contains(dpsBaseAssembly, AssemblyComparer.Instance))
                        catalog.Catalogs.Add(new AssemblyCatalog(ass));
                });

                System.Diagnostics.Debug.Print(dpsBaseAssembly.FullName);

                //Keep track of all assemblies that have been loaded
                var allAssemblies = AppDomain.CurrentDomain.GetAssemblies().ToDictionary(ass => ass.FullName, ass => ass);
                //And all the assembly names we have tried to load
                var loadedAssemblies = new List<string>();

                //Set an identifier to come back to as we load assemblies
                AssemblySearchStart:

                //Loop through all assemblies
                foreach (var pair in allAssemblies)
                {
                    var assembly = pair.Value;

                    //Loop through the assemblies this assemlby references
                    foreach (var referencedAssembly in assembly.GetReferencedAssemblies())
                    {
                        //If we've already tried this assembly name then keep going.  Otherwise record that we will have tried this assembly
                        if (loadedAssemblies.Contains(referencedAssembly.FullName))
                            continue;
                        else
                            loadedAssemblies.Add(referencedAssembly.FullName);

                        //Do a reflection only load of the assembly so that we can see if it references DPSBase and also what it does reference
                        var refAssembly = System.Reflection.Assembly.ReflectionOnlyLoad(referencedAssembly.FullName);

                        //Note that multiple assembly names/versions could resolve to this assembly so check if we're already considered the actual
                        //loaded assembly
                        if (!allAssemblies.ContainsKey(refAssembly.FullName))
                        {
                            //if not add it to the considered list
                            allAssemblies.Add(refAssembly.FullName, refAssembly);

                            //if it references DPSBase directly it might contain components. Add the assembly to the catalog
                            if (refAssembly.GetReferencedAssemblies().Contains(dpsBaseAssembly, AssemblyComparer.Instance))
                            {
                                System.Diagnostics.Debug.Print(refAssembly.FullName);
                                catalog.Catalogs.Add(new AssemblyCatalog(System.Reflection.Assembly.Load(refAssembly.GetName())));
                            }

                            //We're changed allAssemblies and loadedAssemblies so restart
                            goto AssemblySearchStart;
                        }
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
            catch (Exception ex)
            {
                using (StreamWriter sw = new StreamWriter("DPSManagerLoadError.txt", false))
                    sw.WriteLine(ex.ToString());
            }
        }
    }
}
