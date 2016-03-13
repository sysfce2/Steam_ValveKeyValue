﻿using NUnit.Framework;

namespace ValveKeyValue.Test
{
    class ObjectDeserializationTestCase
    {
        [Test]
        public void IsNotNull()
        {
            Assert.That(person, Is.Not.Null);
        }

        [Test]
        public void FirstName()
        {
            Assert.That(person.FirstName, Is.EqualTo("Bob"));
        }

        [Test]
        public void LastName()
        {
            Assert.That(person.LastName, Is.EqualTo("Builder"));
        }

        [Test]
        public void CanFixIt()
        {
            Assert.That(person.CanFixIt, Is.True);
        }

        Person person;

        [OneTimeSetUp]
        public void SetUp()
        {
            using (var stream = TestDataHelper.OpenResource("Text.object_person.vdf"))
            {
                person = KVSerializer.Deserialize<Person>(stream);
            }
        }

        class Person
        {
            public string FirstName { get; set; }

            public string LastName { get; set; }

            public bool CanFixIt { get; set; }
        }
    }
}