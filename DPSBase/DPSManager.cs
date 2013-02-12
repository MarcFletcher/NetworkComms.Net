//  Copyright 2011-2012 Marc Fletcher, Matthew Dean
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
//
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcommsdotnet.com/licenses> for details.

using System;
using System.Collections.Generic;
using System.Text;
using DPSBase;
using System.IO;
using System.Reflection;

#if WINDOWS_PHONE
using MarshalByRefObject = System.Object;
#endif

namespace DPSBase
{
    /// <summary>
    /// Automatically detects and manages the use of <see cref="DataSerializer"/> and <see cref="DataProcessor"/>s.  Any <see cref="DataSerializer"/> or <see cref="DataProcessor"/> in an assembly located in the working directory (including subdirectories) will be automatically detected
    /// </summary>
    public sealed class DPSManager
    {
        #region Comparers
                
        class ReflectedTypeComparer : IEqualityComparer<Type>
        {
            public static ReflectedTypeComparer Instance { get; private set; }

            static ReflectedTypeComparer() { Instance = new ReflectedTypeComparer(); }

            public ReflectedTypeComparer() { }

            #region IEqualityComparer<ICompress> Members

            public bool Equals(Type x, Type y)
            {
                return x.AssemblyQualifiedName == y.AssemblyQualifiedName;
            }

            public int GetHashCode(Type obj)
            {
                return obj.AssemblyQualifiedName.GetHashCode();
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

        private Dictionary<string, bool> AssembliesToLoad = new Dictionary<string, bool>();
        
        private Dictionary<string, DataSerializer> SerializersByType = new Dictionary<string, DataSerializer>();        
        private Dictionary<string, DataProcessor> DataProcessorsByType = new Dictionary<string, DataProcessor>();

        private Dictionary<byte, string> DataSerializerIdToType = new Dictionary<byte, string>();
        private Dictionary<byte, string> DataProcessorIdToType = new Dictionary<byte, string>();

        static DPSManager instance;

        static DPSManager()
        {
            instance = new DPSManager();
        }
        
        /// <summary>
        /// Retrieves the singleton instance of the <see cref="DataSerializer"/> with <see cref="System.Type"/> T
        /// </summary>
        /// <typeparam name="T">The <see cref="System.Type"/> of the <see cref="DataSerializer"/> to retrieve </typeparam>
        /// <returns>The retrieved singleton instance of the desired <see cref="DataSerializer"/></returns>
        public static DataSerializer GetDataSerializer<T>() where T : DataSerializer
        {
            if (instance.SerializersByType.ContainsKey(typeof(T).AssemblyQualifiedName))
                return GetDataSerializer(typeof(T).AssemblyQualifiedName);
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
            if (instance.DataSerializerIdToType.ContainsKey(Id))
                return GetDataSerializer(instance.DataSerializerIdToType[Id]);
            else
                return null;
        }

        private static DataSerializer GetDataSerializer(string t)
        {
            var serializer = instance.SerializersByType[t];

            if (serializer == null)
            {
                //var assembly = System.Reflection.Assembly.Load(t.Assembly.GetName());
                var typeToCreate = Type.GetType(t);

                var constructor = typeToCreate.GetConstructor(BindingFlags.Instance, null, new Type[] { }, null);

                if (constructor == null)
                    constructor = typeToCreate.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { }, null);

                serializer = constructor.Invoke(null) as DataSerializer;

                instance.SerializersByType[t] = serializer;
            }

            return serializer;            
        }
                
        /// <summary>
        /// Retrieves the singleton instance of the <see cref="DataProcessor"/> with <see cref="System.Type"/> T
        /// </summary>
        /// <typeparam name="T">The <see cref="System.Type"/> of the <see cref="DataProcessor"/> to retrieve </typeparam>
        /// <returns>The retrieved singleton instance of the desired <see cref="DataProcessor"/></returns>
        public static DataProcessor GetDataProcessor<T>() where T : DataProcessor
        {
            if (instance.DataProcessorsByType.ContainsKey(typeof(T).AssemblyQualifiedName))
                return GetDataProcessor(typeof(T).AssemblyQualifiedName);
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
            if (instance.DataProcessorIdToType.ContainsKey(Id))
                return GetDataProcessor(instance.DataProcessorIdToType[Id]);
            else
                return null;
        }

        private static DataProcessor GetDataProcessor(string t)
        {
            var processor = instance.DataProcessorsByType[t];

            if (processor == null)
            {
                //var assembly = System.Reflection.Assembly.Load(t.Assembly.GetName());
                var typeToCreate = Type.GetType(t);

                var constructor = typeToCreate.GetConstructor(BindingFlags.Instance, null, new Type[] { }, null);

                if (constructor == null)
                    constructor = typeToCreate.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { }, null);

                processor = constructor.Invoke(null) as DataProcessor;

                instance.DataProcessorsByType[t] = processor;
            }

            return processor;
        }

        /// <summary>
        /// Allows the addition of <see cref="DataProcessor"/>s which are not autodetected.  Use only if the assmbley in which the <see cref="DataProcessor"/> is defined is not in the working directory (including subfolders) or if automatic detection is not supported on your platform
        /// </summary>
        /// <param name="dataProcessor">The <see cref="DataProcessor"/> to make the <see cref="DPSManager"/> aware of</param>
        /// <exception cref="ArgumentException">Thrown if A different <see cref="DataProcessor"/> of the same <see cref="System.Type"/> or Id has already been added to the <see cref="DPSManager"/></exception>
        public static void AddDataProcessor(DataProcessor dataProcessor)
        {
            if (instance.DataProcessorsByType.ContainsKey(dataProcessor.GetType().AssemblyQualifiedName))
                if (instance.DataProcessorsByType[dataProcessor.GetType().AssemblyQualifiedName] != dataProcessor)
                    throw new ArgumentException("A different DataProcessor of the same Type or Id has already been added to DPSManager");
                else
                    return;

            instance.DataProcessorsByType.Add(dataProcessor.GetType().AssemblyQualifiedName, dataProcessor);
            instance.DataProcessorIdToType.Add(dataProcessor.Identifier, dataProcessor.GetType().AssemblyQualifiedName);
        }

        /// <summary>
        /// Allows the addition of <see cref="DataProcessor"/>s which are not autodetected.  Use only if the assmbley in which the <see cref="DataProcessor"/> is defined is not in the working directory (including subfolders) or if automatic detection is not supported on your platform
        /// </summary>
        /// <typeparam name="T">The type of <see cref="DataProcessor"/> to make the <see cref="DPSManager"/> aware of</typeparam>
        /// <exception cref="ArgumentException">Thrown if a different <see cref="DataProcessor"/> of the same <see cref="System.Type"/> or Id has already been added to the <see cref="DPSManager"/> or if the <see cref="DataProcessor"/> type does not have a hidden or visible paramerterless constructor</exception>
        public static void AddDataProcessor<T>() where T : DataProcessor
        {
            Type processorType = typeof(T);

            if (instance.DataProcessorsByType.ContainsKey(processorType.AssemblyQualifiedName))
                return;

            var constructor = processorType.GetConstructor(BindingFlags.Instance, null, new Type[] { }, null);

            if (constructor == null)
                constructor = processorType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { }, null);

            if (constructor == null)
                throw new ArgumentException("Data processors should have a paramerterless constructor");

            DataProcessor res = constructor.Invoke(null) as DataProcessor;

            AddDataProcessor(res);
        }

        /// <summary>
        /// Allows the addition of <see cref="DataSerializer"/>s which are not autodetected.  Use only if the assmbley in which the <see cref="DataSerializer"/> is defined is not in the working directory (including subfolders) or if automatic detection is not supported on your platform
        /// </summary>
        /// <param name="dataSerializer">The <see cref="DataSerializer"/> to make the see <see cref="DPSManager"/> aware of</param>
        /// <exception cref="ArgumentException">Thrown if A different <see cref="DataSerializer"/> of the same <see cref="System.Type"/> or Id has already been added to the <see cref="DPSManager"/></exception>
        public static void AddDataSerializer(DataSerializer dataSerializer)
        {
            if (instance.SerializersByType.ContainsKey(dataSerializer.GetType().AssemblyQualifiedName))
                if (instance.SerializersByType[dataSerializer.GetType().AssemblyQualifiedName] != dataSerializer)
                    throw new ArgumentException("A different DataSerializer of the same Type or Id has already been added to DPSManager");
                else
                    return;

            instance.SerializersByType.Add(dataSerializer.GetType().AssemblyQualifiedName, dataSerializer);
            instance.DataSerializerIdToType.Add(dataSerializer.Identifier, dataSerializer.GetType().AssemblyQualifiedName);
        }

        /// <summary>
        /// Allows the addition of <see cref="DataSerializer"/>s which are not autodetected.  Use only if the assmbley in which the <see cref="DataSerializer"/> is defined is not in the working directory (including subfolders) or if automatic detection is not supported on your platform
        /// </summary>
        /// <typeparam name="T">The type of <see cref="DataSerializer"/> to make the <see cref="DPSManager"/> aware of</typeparam>
        /// <exception cref="ArgumentException">Thrown if a different <see cref="DataSerializer"/> of the same <see cref="System.Type"/> or Id has already been added to the <see cref="DPSManager"/> or if the <see cref="DataSerializer"/> type does not have a hidden or visible paramerterless constructor</exception>
        public static void AddDataSerializer<T>() where T : DataSerializer
        {
            Type serializerType = typeof(T);

            if (instance.SerializersByType.ContainsKey(serializerType.AssemblyQualifiedName))
                return;

            var constructor = serializerType.GetConstructor(BindingFlags.Instance, null, new Type[] { }, null);

            if (constructor == null)
                constructor = serializerType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { }, null);

            if (constructor == null)
                throw new ArgumentException("Data processors should have a paramerterless constructor");

            DataSerializer res = constructor.Invoke(null) as DataSerializer;

            AddDataSerializer(res);
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
            //This constructor loops through referenced assemblies looking for types that inherit off of DataSerializer and DataProcessor.  On windows this should mean perfect autodetection
            //of serializers and compressors. On windows phone we cannot get a list of referenced assemblies so we can only do this for already loaded assemblies.  Any others that are used will
            //have to be added manually.  On windows this will be done from a new app domain so we can unload it afterwards

#if !WINDOWS_PHONE

            AppDomain tempDomain = AppDomain.CreateDomain("Temp");
            var loader = (AssemblyLoader)tempDomain.CreateInstanceAndUnwrap(typeof(AssemblyLoader).Assembly.FullName, typeof(AssemblyLoader).FullName);

            var args = new ProcessArgument();
            args.entryDomain = Assembly.GetEntryAssembly().FullName;

            loader.ProcessApplicationAssemblies(args);

            //app.DoCallBack(new CrossAppDomainDelegate(loader.ProcessApplicationAssemblies));
            AppDomain.Unload(tempDomain);
            tempDomain = null;
            GC.Collect();

#else
            var loader = new AssemblyLoader();
            var args = new ProcessArgument();

            loader.ProcessApplicationAssemblies(args);

#endif

            foreach (var serializer in args.serializerTypes)
            {
                SerializersByType.Add(serializer.Value, null);
                DataSerializerIdToType.Add(serializer.Key, serializer.Value);
            }

            foreach (var processor in args.processorTypes)
            {
                DataProcessorsByType.Add(processor.Value, null);
                DataProcessorIdToType.Add(processor.Key, processor.Value);
            }
        }

        private class ProcessArgument : MarshalByRefObject
        {
            public string entryDomain;
            public Dictionary<byte, string> serializerTypes;// = new Dictionary<byte, string>();
            public Dictionary<byte, string> processorTypes;// = new Dictionary<byte, string>();
        }

        private class AssemblyLoader : MarshalByRefObject
        {
            public Dictionary<byte, string> serializerTypes = new Dictionary<byte, string>();
            public Dictionary<byte, string> processorTypes = new Dictionary<byte,string>();

            public void ProcessApplicationAssemblies(ProcessArgument args)
            {
                try
                {

#if !WINDOWS_PHONE
                    AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += CurrentDomain_ReflectionOnlyAssemblyResolve;
                    AppDomain.CurrentDomain.Load(args.entryDomain);                    
#endif

                    //Store the serializer and processor types as we will need then repeatedly
                    var serializerType = typeof(DPSBase.DataSerializer);
                    var processorType = typeof(DPSBase.DataProcessor);

                    //We're now going to look through the assemly reference tree to look for more components
                    //This will be done by first checking whether a relefection only load of each assembly and checking 
                    //for reference to DPSBase.  We will therefore get a reference to DPSBase
                    var dpsBaseAssembly = typeof(DPSManager).Assembly;

                    //Loop through all loaded assemblies looking for types that are not abstract and implement DataProcessor or DataSerializer.  They also need to have a paramterless contstructor                
                    var alreadyLoadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

                    //We are also going to keep a track of all assemblies with which we have considered types within
                    var dicOfSearchedAssemblies = new Dictionary<string, Assembly>();

                    //And all the assembly names we have tried to load
                    var listofConsideredAssemblies = new List<string>();

                    foreach (var ass in alreadyLoadedAssemblies)
                    {
#if WINDOWS_PHONE
#else
                        foreach (var refAss in ass.GetReferencedAssemblies())
                        {
                            if (AssemblyComparer.Instance.Equals(dpsBaseAssembly.GetName(), refAss) || ass == dpsBaseAssembly)
                            {
#endif
                                foreach (var type in ass.GetTypes())
                                {
                                    byte id;
                                    var attributes = type.GetCustomAttributes(typeof(DataSerializerProcessorAttribute), false);

                                    if (attributes.Length == 1)
                                    {
                                        id = (attributes[0] as DataSerializerProcessorAttribute).Identifier;
                                    }
                                    else
                                        continue;

                                    if (serializerType.IsAssignableFrom(type) && !type.IsAbstract &&
                                        (type.GetConstructors(BindingFlags.Instance).Length != 0 || type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Length != 0))
                                    {
                                        //SerializersByType.Add(type, null);
                                        //DataSerializerIdToType.Add(id, type);
                                        serializerTypes.Add(id, type.AssemblyQualifiedName);
                                    }

                                    if (processorType.IsAssignableFrom(type) && !type.IsAbstract &&
                                        (type.GetConstructors(BindingFlags.Instance).Length != 0 || type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Length != 0))
                                    {
                                        //DataProcessorsByType.Add(type, null);
                                        //DataProcessorIdToType.Add(id, type);
                                        processorTypes.Add(id, type.AssemblyQualifiedName);
                                    }
                                }
#if WINDOWS_PHONE
#else
                                break;
                            }
                        }
#endif
                        dicOfSearchedAssemblies.Add(ass.FullName, ass);
                    }

#if WINDOWS_PHONE
#else
                //Set an identifier to come back to as we load assemblies
                AssemblySearchStart:

                    //Loop through all assemblies
                    foreach (var pair in dicOfSearchedAssemblies)
                    {
                        var assembly = pair.Value;

                        //Loop through the assemblies this assemlby references
                        foreach (var referencedAssembly in assembly.GetReferencedAssemblies())
                        {
                            //If we've already tried this assembly name then keep going.  Otherwise record that we will have tried this assembly
                            if (listofConsideredAssemblies.Contains(referencedAssembly.FullName))
                                continue;
                            else
                                listofConsideredAssemblies.Add(referencedAssembly.FullName);

                            //Do a reflection only load of the assembly so that we can see if it references DPSBase and also what it does reference
                            var refAssembly = System.Reflection.Assembly.ReflectionOnlyLoad(referencedAssembly.FullName);

                            //Note that multiple assembly names/versions could resolve to this assembly so check if we're already considered the actual
                            //loaded assembly
                            if (!dicOfSearchedAssemblies.ContainsKey(refAssembly.FullName))
                            {
                                //if not add it to the considered list
                                dicOfSearchedAssemblies.Add(refAssembly.FullName, refAssembly);

                                //if it references DPSBase directly it might contain components. Add the assembly to the catalog
                                foreach (var refAss in refAssembly.GetReferencedAssemblies())
                                {
                                    if (AssemblyComparer.Instance.Equals(dpsBaseAssembly.GetName(), refAss))
                                    {
                                        foreach (var type in refAssembly.GetTypes())
                                        {
                                            bool idSet = false;
                                            byte id = 0;
                                            //var attributes = type.GetCustomAttributes(typeof(DataSerializerProcessorAttribute), false);
                                            var attributes = CustomAttributeData.GetCustomAttributes(type);

                                            foreach (var attr in attributes)
                                            {
                                                if (attr.Constructor.ReflectedType.AssemblyQualifiedName == typeof(DataSerializerProcessorAttribute).AssemblyQualifiedName)
                                                {
                                                    id = (byte)attr.ConstructorArguments[0].Value;
                                                    idSet = true;
                                                }
                                            }

                                            if (!idSet)
                                                continue;

                                            Type baseType = type.BaseType;

                                            while (baseType != null)
                                            {
                                                if (baseType.AssemblyQualifiedName == serializerType.AssemblyQualifiedName)
                                                {
                                                    //SerializersByType.Add(type, null);
                                                    //DataSerializerIdToType.Add(id, type);
                                                    serializerTypes.Add(id, type.AssemblyQualifiedName);
                                                    break;
                                                }

                                                if (baseType.AssemblyQualifiedName == processorType.AssemblyQualifiedName)
                                                {
                                                    //DataProcessorsByType.Add(type, null);
                                                    //DataProcessorIdToType.Add(id, type);
                                                    processorTypes.Add(id, type.AssemblyQualifiedName);
                                                    break;
                                                }

                                                baseType = baseType.BaseType;
                                            }
                                        }

                                        break;
                                    }
                                }

                                //We're changed allAssemblies and loadedAssemblies so restart
                                goto AssemblySearchStart;
                            }
                        }
                    }
#endif
                }
                catch (Exception ex)
                {
                    //using (StreamWriter sw = new StreamWriter("DPSManagerLoadError.txt", false))
                    //    sw.WriteLine(ex.ToString());
                }
                finally
                {
                    args.processorTypes = processorTypes;
                    args.serializerTypes = serializerTypes;
                }
            }

#if !WINDOWS_PHONE
            Assembly CurrentDomain_ReflectionOnlyAssemblyResolve(object sender, ResolveEventArgs args)
            {
                return Assembly.ReflectionOnlyLoad(args.Name); 
            }
#endif
        }
    }
}
