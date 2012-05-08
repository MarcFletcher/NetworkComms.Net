using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition.Hosting;
using SerializerBase;
using System.ComponentModel.Composition;

namespace SerializerBase
{
    public class SerializerCompressorLoadingHelper
    {
        private CompositionContainer _container;

        [ImportMany(typeof(ISerialize))]
        private IEnumerable<ISerialize> serializers;

        [ImportMany(typeof(ICompress))]
        private IEnumerable<ICompress> compressors;

        private Dictionary<Type, ISerialize> Serializers = new Dictionary<Type, ISerialize>();
        private Dictionary<Type, ICompress> Compressors = new Dictionary<Type, ICompress>();

        static SerializerCompressorLoadingHelper instance;
        
        public static SerializerCompressorLoadingHelper Instance { get { return instance; } }

        static SerializerCompressorLoadingHelper()
        {
            instance = new SerializerCompressorLoadingHelper();
        }

        public Dictionary<Type, ISerialize> GetAllSerializes()
        {
            return Serializers;
        }

        public Dictionary<Type, ICompress> GetAllCompressors()
        {
            return Compressors;
        }

        private SerializerCompressorLoadingHelper()
        {            
            //An aggregate catalog that combines multiple catalogs
            var catalog = new AggregateCatalog();
            //Adds all the parts found in dlls in the same localtion
            catalog.Catalogs.Add(new DirectoryCatalog(".//", "*.dll"));
                        
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

            Serializers = serializers.ToDictionary(l => l.GetType(), l => l);
            Compressors = compressors.ToDictionary(l => l.GetType(), l => l);
        }
    }
}
