using NTDLS.CatMQ.Shared;

namespace CatMQ.Tests.Unit
{
    public class ListPushFirstAndGet(ServerFixture fixture) : IClassFixture<ServerFixture>
    {
        [Fact(DisplayName = "PushFirst values to List (Single).")]
        public void TestPersistentListOfSingles()
        {
            var client = ClientFactory.CreateAndConnect();

            /*
            var keyStoreName = "Test.ListOfSingles.ListPushFirstAndGet";

            client.CreateStore(new KkStoreConfiguration(keyStoreName)
            {
                CacheExpiration = TimeSpan.FromMinutes(1),
                PersistenceScheme = KkPersistenceScheme.Persistent,
                ValueType = KkValueType.ListOfSingles
            });

            float pushValue = 0;

            //Push values to the bottom of the list.
            for (int i = 0; i < 100; i++)
            {
                client.PushFirst(keyStoreName, "TestValueList", pushValue);
                var values = client.GetList<float>(keyStoreName, "TestValueList");

                Assert.NotNull(values);
                Assert.Equal(i + 1, values.Count);

                float testValue = (values.Count - 1) * 0.5f;
                for (long t = values.Count; t > 0; t--)
                {
                    Assert.Equal(testValue, values[(int)(values.Count - t)].Value);
                    testValue -= 0.5f;
                }

                pushValue += 0.5f;
            }

            //Flush the cache so we can test persistence.
            client.FlushCache(keyStoreName);

            var postFlushValues = client.GetList<Single>(keyStoreName, "TestValueList");
            Assert.NotNull(postFlushValues);

            float pfTestValue = (postFlushValues.Count - 1) * 0.5f;
            for (int pfIndex = 0; pfIndex < postFlushValues.Count; pfIndex++)
            {
                Assert.Equal(pfTestValue, postFlushValues[pfIndex].Value);
                pfTestValue -= 0.5f;
            }
            */

            client.Disconnect();
        }
    }
}
