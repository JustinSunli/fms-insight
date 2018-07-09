/* Copyright (c) 2018, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
using System;
using System.Collections.Generic;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;
using System.Data;
using System.Linq;
using MazakMachineInterface;
using Xunit;
using FluentAssertions;

namespace MachineWatchTest
{
	public class PalletMap
	{
		[Fact]
		public void Basic()
		{
			//Test everything copied from the template

			var job1 = new JobPlan("Job1", 1, new int[] {2});
			job1.PartName = "Part1";
			job1.AddProcessOnPallet(1, 1, "4");
			job1.AddProcessOnPallet(1, 1, "5");
			job1.AddProcessOnPallet(1, 2, "10");
			job1.AddProcessOnPallet(1, 2, "11");
			job1.AddProcessOnPallet(1, 2, "12");
			job1.AddLoadStation(1, 1, 1);
			job1.AddLoadStation(1, 1, 2);
			job1.AddLoadStation(1, 2, 5);
			job1.AddUnloadStation(1, 1, 4);
			job1.AddUnloadStation(1, 2, 3);
			var stop = new JobMachiningStop("machine");
			stop.AddProgram(1, "");
			job1.AddMachiningStop(1, 1, stop);
			stop = new JobMachiningStop("machine");
			stop.AddProgram(3, "");
			stop.AddProgram(4, "");
			job1.AddMachiningStop(1, 2, stop);

			var job2 = new JobPlan("Job2", 1, new int[] {2});
			job2.PartName = "Part2";
			job2.AddProcessOnPallet(1, 1, "4");
			job2.AddProcessOnPallet(1, 1, "5");
			job2.AddProcessOnPallet(1, 2, "10");
			job2.AddProcessOnPallet(1, 2, "11");
			job2.AddProcessOnPallet(1, 2, "12");

			var job3 = new JobPlan("Job3", 1, new int[] {1});
			job3.PartName = "Part3";
			job3.AddProcessOnPallet(1, 1, "20");
			job3.AddProcessOnPallet(1, 1, "21");

			var job4 = new JobPlan("Job4", 1, new int[] {1});
			job4.PartName = "Part4";
			job4.AddProcessOnPallet(1, 1, "20");
			job4.AddProcessOnPallet(1, 1, "21");

			var log = new List<string>();
			var trace = new List<string>();

			var dset = CreateReadSet();

			CreatePart(dset, "Job1", "Part1", 2, "Test");
			CreatePart(dset, "Job2", "Part2", 2, "Test");
			CreatePart(dset, "Job3", "Part3", 1, "Test");
			CreatePart(dset, "Job4", "Part4", 1, "Test");

			var pMap = new clsPalletPartMapping(new JobPlan[] {job1, job2, job3, job4}, dset, 3,
			                                    new List<string>(), log, trace, true, "NewGlobal",
			                                    false, DatabaseAccess.MazakDbType.MazakVersionE);

			//Console.WriteLine(DatabaseAccess.Join(trace, Environment.NewLine));
			if (log.Count > 0) Assert.True(false, log[0]);

			CheckNewFixtures(pMap, new string[] {
				"Fixt:3:0:4:1",
				"Fixt:3:0:4:2",
				"Fixt:3:1:10:1",
				"Fixt:3:1:10:2",
				"Fixt:3:2:20:1"
			});

			var trans = new TransactionDataSet();
			pMap.CreateRows(trans);

			CheckPartProcess(trans, "Part1:3:1", 1, "Fixt:3:0:4:1", "1200000000", "0004000000", "10000000");
			CheckPartProcess(trans, "Part1:3:1", 2, "Fixt:3:0:4:2", "1200000000", "0004000000", "10000000");
			CheckPart(trans, "Part1:3:1", "Job1-Path1-0");

			CheckPartProcess(trans, "Part1:3:2", 1, "Fixt:3:1:10:1", "0000500000", "0030000000", "00340000");
			CheckPartProcess(trans, "Part1:3:2", 2, "Fixt:3:1:10:2", "0000500000", "0030000000", "00340000");
			CheckPart(trans, "Part1:3:2", "Job1-Path2-0");

			CheckPartProcess(trans, "Part2:3:1", 1, "Fixt:3:0:4:1");
			CheckPartProcess(trans, "Part2:3:1", 2, "Fixt:3:0:4:2");
			CheckPart(trans, "Part2:3:1", "Job2-Path1-0");

			CheckPartProcess(trans, "Part2:3:2", 1, "Fixt:3:1:10:1");
			CheckPartProcess(trans, "Part2:3:2", 2, "Fixt:3:1:10:2");
			CheckPart(trans, "Part2:3:2", "Job2-Path2-0");

			CheckPartProcess(trans, "Part3:3:1", 1, "Fixt:3:2:20:1");
			CheckPart(trans, "Part3:3:1", "Job3-Path1-0");

			CheckPartProcess(trans, "Part4:3:1", 1, "Fixt:3:2:20:1");
			CheckPart(trans, "Part4:3:1", "Job4-Path1-0");

			CheckPalletGroup(trans, 31, "Fixt:3:0:4", 2, new int[] {4, 5});
			CheckPalletGroup(trans, 32, "Fixt:3:1:10", 2, new int[] {10, 11, 12});
			CheckPalletGroup(trans, 33, "Fixt:3:2:20", 1, new int[] {20, 21});

			AssertPartsPalletsDeleted(trans);
		}

		[Fact]
		public void UseExistingFixture()
		{
			var job1 = new JobPlan("Job1", 1, new int[] {2});
			job1.PartName = "Part1";
			job1.AddProcessOnPallet(1, 1, "4");
			job1.AddProcessOnPallet(1, 1, "5");
			job1.AddProcessOnPallet(1, 2, "10");
			job1.AddProcessOnPallet(1, 2, "11");
			job1.AddProcessOnPallet(1, 2, "12");

			var job2 = new JobPlan("Job2", 1, new int[] {2});
			job2.PartName = "Part2";
			job2.AddProcessOnPallet(1, 1, "4");
			job2.AddProcessOnPallet(1, 1, "5");
			job2.AddProcessOnPallet(1, 2, "10");
			job2.AddProcessOnPallet(1, 2, "11");
			job2.AddProcessOnPallet(1, 2, "12");

			var job3 = new JobPlan("Job3", 1, new int[] {1});
			job3.PartName = "Part3";
			job3.AddProcessOnPallet(1, 1, "20");
			job3.AddProcessOnPallet(1, 1, "21");

			var job4 = new JobPlan("Job4", 1, new int[] {1});
			job4.PartName = "Part4";
			job4.AddProcessOnPallet(1, 1, "20");
			job4.AddProcessOnPallet(1, 1, "21");

			var log = new List<string>();
			var trace = new List<string>();

			var dset = CreateReadSet();

			CreatePart(dset, "Job1", "Part1", 2, "Test");
			CreatePart(dset, "Job2", "Part2", 2, "Test");
			CreatePart(dset, "Job3", "Part3", 1, "Test");
			CreatePart(dset, "Job4", "Part4", 1, "Test");

			//Create fixtures which match for Parts 1 and 2.
			var savedParts = new List<string>();
			CreateFixture(dset, "Fixt:2:0:4:1");
			CreateFixture(dset, "Fixt:2:0:4:2");
			CreateFixture(dset, "Fixt:2:0:10:1");
			CreateFixture(dset, "Fixt:2:0:10:2");
			CreatePart(dset, "Job1.0", "Part1:2:1", 2, "Fixt:2:0:4");
			savedParts.Add("Part1:2:1");
			CreatePart(dset, "Job1.0", "Part1:2:2", 2, "Fixt:2:0:10");
			savedParts.Add("Part1:2:2");
			CreatePallet(dset, 4, "Fixt:2:0:4", 2);
			CreatePallet(dset, 5, "Fixt:2:0:4", 2);
			CreatePallet(dset, 10, "Fixt:2:0:10", 2);
			CreatePallet(dset, 11, "Fixt:2:0:10", 2);
			CreatePallet(dset, 12, "Fixt:2:0:10", 2);

			//Create several fixtures which almost but not quite match for parts 3 and 4.

			//group with an extra pallet
			CreateFixture(dset, "Fixt:1:0:20:1");
			CreatePart(dset, "Job3.0", "Part3:1:1", 1, "Fixt:1:0:20");
			savedParts.Add("Part3:1:1");
			CreatePallet(dset, 20, "Fixt:1:0:20", 1);
			CreatePallet(dset, 21, "Fixt:1:0:20", 1);
			CreatePallet(dset, 22, "Fixt:1:0:20", 1);

			//group with a missing pallet
			CreateFixture(dset, "Fixt:7:0:20:1");
			CreatePart(dset, "Job3.1", "Part3:7:1", 1, "Fixt:7:0:20");
			savedParts.Add("Part3:7:1");
			CreatePallet(dset, 20, "Fixt:7:0:20", 1);

			//group with a different number of processes
			CreateFixture(dset, "Fixt:9:0:20:1");
			CreateFixture(dset, "Fixt:9:0:20:2");
			CreatePart(dset, "Job3.2", "Part3:9:1", 2, "Fixt:9:0:20");
			savedParts.Add("Part3:9:1");
			CreatePallet(dset, 20, "Fixt:9:0:20", 2);
			CreatePallet(dset, 21, "Fixt:9:0:20", 2);

			var pMap = new clsPalletPartMapping(new JobPlan[] {job1, job2, job3, job4}, dset, 3,
			                                    savedParts, log, trace, true, "NewGlobal",
			                                    false,  DatabaseAccess.MazakDbType.MazakVersionE);

			//Console.WriteLine(DatabaseAccess.Join(trace, Environment.NewLine));
			if (log.Count > 0) Assert.True(false, log[0]);

			CheckNewFixtures(pMap, new string[] {
				"Fixt:3:2:20:1"
			});

			var trans = new TransactionDataSet();
			pMap.CreateRows(trans);

			CheckPartProcess(trans, "Part1:3:1", 1, "Fixt:2:0:4:1");
			CheckPartProcess(trans, "Part1:3:1", 2, "Fixt:2:0:4:2");
			CheckPart(trans, "Part1:3:1", "Job1-Path1-0");

			CheckPartProcess(trans, "Part1:3:2", 1, "Fixt:2:0:10:1");
			CheckPartProcess(trans, "Part1:3:2", 2, "Fixt:2:0:10:2");
			CheckPart(trans, "Part1:3:2", "Job1-Path2-0");

			CheckPartProcess(trans, "Part2:3:1", 1, "Fixt:2:0:4:1");
			CheckPartProcess(trans, "Part2:3:1", 2, "Fixt:2:0:4:2");
			CheckPart(trans, "Part2:3:1", "Job2-Path1-0");

			CheckPartProcess(trans, "Part2:3:2", 1, "Fixt:2:0:10:1");
			CheckPartProcess(trans, "Part2:3:2", 2, "Fixt:2:0:10:2");
			CheckPart(trans, "Part2:3:2", "Job2-Path2-0");

			CheckPartProcess(trans, "Part3:3:1", 1, "Fixt:3:2:20:1");
			CheckPart(trans, "Part3:3:1", "Job3-Path1-0");

			CheckPartProcess(trans, "Part4:3:1", 1, "Fixt:3:2:20:1");
			CheckPart(trans, "Part4:3:1", "Job4-Path1-0");

			CheckPalletGroup(trans, 33, "Fixt:3:2:20", 1, new int[] {20, 21});

			AssertPartsPalletsDeleted(trans);
		}

		[Fact]
		public void MultiProcess()
		{
			//A test where Jobs have different number of processes but the same pallet list

			var job1 = new JobPlan("Job1", 1, new int[] {2});
			job1.PartName = "Part1";
			job1.AddProcessOnPallet(1, 1, "4");
			job1.AddProcessOnPallet(1, 1, "5");
			job1.AddProcessOnPallet(1, 2, "10");
			job1.AddProcessOnPallet(1, 2, "11");
			job1.AddProcessOnPallet(1, 2, "12");

			var job2 = new JobPlan("Job2", 1, new int[] {2});
			job2.PartName = "Part2";
			job2.AddProcessOnPallet(1, 1, "4");
			job2.AddProcessOnPallet(1, 1, "5");
			job2.AddProcessOnPallet(1, 2, "10");
			job2.AddProcessOnPallet(1, 2, "11");
			job2.AddProcessOnPallet(1, 2, "12");

			var job3 = new JobPlan("Job3", 1, new int[] {1});
			job3.PartName = "Part3";
			job3.AddProcessOnPallet(1, 1, "20");
			job3.AddProcessOnPallet(1, 1, "21");

			var job4 = new JobPlan("Job4", 1, new int[] {1});
			job4.PartName = "Part4";
			job4.AddProcessOnPallet(1, 1, "20");
			job4.AddProcessOnPallet(1, 1, "21");

			var log = new List<string>();
			var trace = new List<string>();

			var dset = CreateReadSet();

			CreatePart(dset, "Job1", "Part1", 2, "Test");
			CreatePart(dset, "Job2", "Part2", 3, "Test");
			CreatePart(dset, "Job3", "Part3", 1, "Test");
			CreatePart(dset, "Job4", "Part4", 1, "Test");

			var pMap = new clsPalletPartMapping(new JobPlan[] {job1, job2, job3, job4}, dset, 3,
			                                    new List<string>(), log, trace, true, "NewGlobal",
			                                    false,  DatabaseAccess.MazakDbType.MazakVersionE);

			//Console.WriteLine(DatabaseAccess.Join(trace, Environment.NewLine));
			if (log.Count > 0) Assert.True(false, log[0]);

			CheckNewFixtures(pMap, new string[] {
				"Fixt:3:0:4:1",
				"Fixt:3:0:4:2",
				"Fixt:3:0:4:3",
				"Fixt:3:1:10:1",
				"Fixt:3:1:10:2",
				"Fixt:3:1:10:3",
				"Fixt:3:2:20:1"
			});

			var trans = new TransactionDataSet();
			pMap.CreateRows(trans);

			CheckPartProcess(trans, "Part1:3:1", 1, "Fixt:3:0:4:1");
			CheckPartProcess(trans, "Part1:3:1", 2, "Fixt:3:0:4:2");
			CheckPart(trans, "Part1:3:1", "Job1-Path1-0");

			CheckPartProcess(trans, "Part1:3:2", 1, "Fixt:3:1:10:1");
			CheckPartProcess(trans, "Part1:3:2", 2, "Fixt:3:1:10:2");
			CheckPart(trans, "Part1:3:2", "Job1-Path2-0");

			CheckPartProcess(trans, "Part2:3:1", 1, "Fixt:3:0:4:1");
			CheckPartProcess(trans, "Part2:3:1", 2, "Fixt:3:0:4:2");
			CheckPartProcess(trans, "Part2:3:1", 3, "Fixt:3:0:4:3");
			CheckPart(trans, "Part2:3:1", "Job2-Path1-0");

			CheckPartProcess(trans, "Part2:3:2", 1, "Fixt:3:1:10:1");
			CheckPartProcess(trans, "Part2:3:2", 2, "Fixt:3:1:10:2");
			CheckPartProcess(trans, "Part2:3:2", 3, "Fixt:3:1:10:3");
			CheckPart(trans, "Part2:3:2", "Job2-Path2-0");

			CheckPartProcess(trans, "Part3:3:1", 1, "Fixt:3:2:20:1");
			CheckPart(trans, "Part3:3:1", "Job3-Path1-0");

			CheckPartProcess(trans, "Part4:3:1", 1, "Fixt:3:2:20:1");
			CheckPart(trans, "Part4:3:1", "Job4-Path1-0");

			CheckPalletGroup(trans, 31, "Fixt:3:0:4", 3, new int[] {4, 5});
			CheckPalletGroup(trans, 32, "Fixt:3:1:10", 3, new int[] {10, 11, 12});
			CheckPalletGroup(trans, 33, "Fixt:3:2:20", 1, new int[] {20, 21});

			AssertPartsPalletsDeleted(trans);

		}

		[Fact]
		public void NonOverlappingPallets()
		{
			CheckNonOverlapping(false);

			try {

				CheckNonOverlapping(true);

				Assert.True(false, "Was expecting an exception");

			} catch (Exception ex) {
				Assert.Equal("Invalid pallet->part mapping. Part1 and Part2 do not " +
				                "have matching pallet lists.  Part1 is assigned to 4,5" +
				                " and Part2 is assigned to 4,5,6",
				                ex.Message);
			}

		}

		private void CheckNonOverlapping(bool checkPalletUsedOnce)
		{
			var job1 = new JobPlan("Job1", 1, new int[] {2});
			job1.PartName = "Part1";
			job1.AddProcessOnPallet(1, 1, "4");
			job1.AddProcessOnPallet(1, 1, "5");
			job1.AddProcessOnPallet(1, 2, "10");
			job1.AddProcessOnPallet(1, 2, "11");
			job1.AddProcessOnPallet(1, 2, "12");

			var job2 = new JobPlan("Job2", 1, new int[] {2});
			job2.PartName = "Part2";
			job2.AddProcessOnPallet(1, 1, "4");
			job2.AddProcessOnPallet(1, 1, "5");
			job2.AddProcessOnPallet(1, 1, "6");
			job2.AddProcessOnPallet(1, 2, "10");
			job2.AddProcessOnPallet(1, 2, "11");
			job2.AddProcessOnPallet(1, 2, "12");

			var log = new List<string>();
			var trace = new List<string>();

			var dset = CreateReadSet();

			CreatePart(dset, "Job1", "Part1", 2, "Test");
			CreatePart(dset, "Job2", "Part2", 2, "Test");

			var pMap = new clsPalletPartMapping(new JobPlan[] {job1, job2}, dset, 3,
			                                    new List<string>(), log, trace, true, "NewGlobal",
			                                    checkPalletUsedOnce,  DatabaseAccess.MazakDbType.MazakVersionE);

			//Console.WriteLine(DatabaseAccess.Join(trace, Environment.NewLine));
			if (log.Count > 0) Assert.True(false, log[0]);

			CheckNewFixtures(pMap, new string[] {
				"Fixt:3:0:4:1",
				"Fixt:3:0:4:2",
				"Fixt:3:1:10:1",
				"Fixt:3:1:10:2",
				"Fixt:3:2:4:1",
				"Fixt:3:2:4:2",
			});

			var trans = new TransactionDataSet();
			pMap.CreateRows(trans);

			CheckPartProcess(trans, "Part1:3:1", 1, "Fixt:3:0:4:1");
			CheckPartProcess(trans, "Part1:3:1", 2, "Fixt:3:0:4:2");
			CheckPart(trans, "Part1:3:1", "Job1-Path1-0");

			CheckPartProcess(trans, "Part1:3:2", 1, "Fixt:3:1:10:1");
			CheckPartProcess(trans, "Part1:3:2", 2, "Fixt:3:1:10:2");
			CheckPart(trans, "Part1:3:2", "Job1-Path2-0");

			CheckPartProcess(trans, "Part2:3:1", 1, "Fixt:3:2:4:1");
			CheckPartProcess(trans, "Part2:3:1", 2, "Fixt:3:2:4:2");
			CheckPart(trans, "Part2:3:1", "Job2-Path1-0");

			CheckPartProcess(trans, "Part2:3:2", 1, "Fixt:3:1:10:1");
			CheckPartProcess(trans, "Part2:3:2", 2, "Fixt:3:1:10:2");
			CheckPart(trans, "Part2:3:2", "Job2-Path2-0");

			CheckPalletGroup(trans, 31, "Fixt:3:0:4", 2, new int[] {4, 5});
			CheckPalletGroup(trans, 32, "Fixt:3:1:10", 2, new int[] {10, 11, 12});
			CheckPalletGroup(trans, 33, "Fixt:3:2:4", 2, new int[] {4, 5, 6});

			AssertPartsPalletsDeleted(trans);
		}

		[Fact]
		public void BasicFromJob()
		{
			var job1 = new JobPlan("Job1", 2, new int[] {2, 2});
			job1.PartName = "Part1";
			job1.SetPathGroup(1, 1, 1);
			job1.SetPathGroup(1, 2, 2);
			job1.SetPathGroup(2, 1, 1);
			job1.SetPathGroup(2, 2, 2);

			//proc 1 and proc 2 on same pallets
			job1.AddProcessOnPallet(1, 1, "4");
			job1.AddProcessOnPallet(1, 1, "5");
			job1.AddProcessOnPallet(1, 2, "10");
			job1.AddProcessOnPallet(1, 2, "11");
			job1.AddProcessOnPallet(1, 2, "12");
			job1.AddProcessOnPallet(2, 1, "4");
			job1.AddProcessOnPallet(2, 1, "5");
			job1.AddProcessOnPallet(2, 2, "10");
			job1.AddProcessOnPallet(2, 2, "11");
			job1.AddProcessOnPallet(2, 2, "12");

			AddBasicStopsWithProg(job1);

			var job2 = new JobPlan("Job2", 2, new int[] {2, 2});
			job2.PartName = "Part2";

			//make path groups twisted
			job2.SetPathGroup(1, 1, 1);
			job2.SetPathGroup(1, 2, 2);
			job2.SetPathGroup(2, 1, 2);
			job2.SetPathGroup(2, 2, 1);

			//process groups on the same pallet.
			job2.AddProcessOnPallet(1, 1, "4");
			job2.AddProcessOnPallet(1, 1, "5");
			job2.AddProcessOnPallet(1, 2, "10");
			job2.AddProcessOnPallet(1, 2, "11");
			job2.AddProcessOnPallet(1, 2, "12");
			job2.AddProcessOnPallet(2, 2, "4");
			job2.AddProcessOnPallet(2, 2, "5");
			job2.AddProcessOnPallet(2, 1, "10");
			job2.AddProcessOnPallet(2, 1, "11");
			job2.AddProcessOnPallet(2, 1, "12");

			AddBasicStopsWithProg(job2);

			var job3 = new JobPlan("Job3", 1, new int[] {2});
			job3.PartName = "Part3";
			job3.AddProcessOnPallet(1, 1, "20");
			job3.AddProcessOnPallet(1, 1, "21");
			job3.AddProcessOnPallet(1, 2, "30");
			job3.AddProcessOnPallet(1, 2, "31");

			AddBasicStopsWithProg(job3);

			//make Job 4 a template
			var job4 = new JobPlan("Job4", 1, new int[] {2});
			job4.PartName = "Part4";
			job4.AddProcessOnPallet(1, 1, "20");
			job4.AddProcessOnPallet(1, 1, "21");
			job4.AddProcessOnPallet(1, 2, "30");
			job4.AddProcessOnPallet(1, 2, "31");


			var log = new List<string>();
			var trace = new List<string>();

			var dset = CreateReadSet();

			CreatePart(dset, "Job4", "Part4", 1, "Test");
			CreateProgram(dset, "1234");

			var pMap = new clsPalletPartMapping(new JobPlan[] {job1, job2, job3, job4}, dset, 3,
			                                    new List<string>(), log, trace, true, "NewGlobal",
			                                    false,  DatabaseAccess.MazakDbType.MazakVersionE);

			//Console.WriteLine(DatabaseAccess.Join(trace, Environment.NewLine));
			if (log.Count > 0) Assert.True(false, log[0]);

			CheckNewFixtures(pMap, new string[] {
				"Fixt:3:0:4:1",
				"Fixt:3:0:4:2",
				"Fixt:3:1:10:1",
				"Fixt:3:1:10:2",
				"Fixt:3:2:20:1",
				"Fixt:3:3:30:1"
			});

			var trans = new TransactionDataSet();
			pMap.CreateRows(trans);

			CheckPartProcessFromJob(trans, "Part1:3:1", 1, "Fixt:3:0:4:1");
			CheckPartProcessFromJob(trans, "Part1:3:1", 2, "Fixt:3:0:4:2");
			CheckPart(trans, "Part1:3:1", "Job1-Path1-0");

			CheckPartProcessFromJob(trans, "Part1:3:2", 1, "Fixt:3:1:10:1");
			CheckPartProcessFromJob(trans, "Part1:3:2", 2, "Fixt:3:1:10:2");
			CheckPart(trans, "Part1:3:2", "Job1-Path2-0");

			CheckPartProcessFromJob(trans, "Part2:3:1", 1, "Fixt:3:0:4:1");
			CheckPartProcessFromJob(trans, "Part2:3:1", 2, "Fixt:3:0:4:2");
			CheckPart(trans, "Part2:3:1", "Job2-Path1-0");

			CheckPartProcessFromJob(trans, "Part2:3:2", 1, "Fixt:3:1:10:1");
			CheckPartProcessFromJob(trans, "Part2:3:2", 2, "Fixt:3:1:10:2");
			CheckPart(trans, "Part2:3:2", "Job2-Path2-0");

			CheckPartProcessFromJob(trans, "Part3:3:1", 1, "Fixt:3:2:20:1");
			CheckPart(trans, "Part3:3:1", "Job3-Path1-0");

			CheckPartProcessFromJob(trans, "Part3:3:2", 1, "Fixt:3:3:30:1");
			CheckPart(trans, "Part3:3:2", "Job3-Path2-0");

			CheckPartProcess(trans, "Part4:3:1", 1, "Fixt:3:2:20:1");
			CheckPart(trans, "Part4:3:1", "Job4-Path1-0");

			CheckPartProcess(trans, "Part4:3:2", 1, "Fixt:3:3:30:1");
			CheckPart(trans, "Part4:3:2", "Job4-Path2-0");

			CheckPalletGroup(trans, 31, "Fixt:3:0:4", 2, new int[] {4, 5});
			CheckPalletGroup(trans, 32, "Fixt:3:1:10", 2, new int[] {10, 11, 12});
			CheckPalletGroup(trans, 33, "Fixt:3:2:20", 1, new int[] {20, 21});
			CheckPalletGroup(trans, 34, "Fixt:3:3:30", 1, new int[] {30, 31});

			AssertPartsPalletsDeleted(trans);
		}

		[Fact]
		public void DifferentPallets()
		{
			//Test when processes have different pallet lists
		    var job1 = new JobPlan("Job1", 2, new int[] {2, 2});
			job1.PartName = "Part1";
			job1.SetPathGroup(1, 1, 1);
			job1.SetPathGroup(1, 2, 2);
			job1.SetPathGroup(2, 1, 1);
			job1.SetPathGroup(2, 2, 2);

			job1.AddProcessOnPallet(1, 1, "4");
			job1.AddProcessOnPallet(1, 1, "5");
			job1.AddProcessOnPallet(1, 2, "10");
			job1.AddProcessOnPallet(1, 2, "11");
			job1.AddProcessOnPallet(1, 2, "12");
			job1.AddProcessOnPallet(2, 1, "40");
			job1.AddProcessOnPallet(2, 1, "50");
			job1.AddProcessOnPallet(2, 2, "100");
			job1.AddProcessOnPallet(2, 2, "110");
			job1.AddProcessOnPallet(2, 2, "120");

			AddBasicStopsWithProg(job1);

			var job2 = new JobPlan("Job2", 2, new int[] {2, 2});
			job2.PartName = "Part2";

			//make path groups twisted
			job2.SetPathGroup(1, 1, 1);
			job2.SetPathGroup(1, 2, 2);
			job2.SetPathGroup(2, 1, 2);
			job2.SetPathGroup(2, 2, 1);

			//process groups on the same pallet.
			job2.AddProcessOnPallet(1, 1, "4");
			job2.AddProcessOnPallet(1, 1, "5");
			job2.AddProcessOnPallet(1, 2, "10");
			job2.AddProcessOnPallet(1, 2, "11");
			job2.AddProcessOnPallet(1, 2, "12");
			job2.AddProcessOnPallet(2, 2, "40");
			job2.AddProcessOnPallet(2, 2, "50");
			job2.AddProcessOnPallet(2, 1, "100");
			job2.AddProcessOnPallet(2, 1, "110");
			job2.AddProcessOnPallet(2, 1, "120");

			AddBasicStopsWithProg(job2);

			var job3 = new JobPlan("Job3", 2, new int[] {2, 2});
			job3.PartName = "Part3";

			job3.SetPathGroup(1, 1, 1);
			job3.SetPathGroup(1, 2, 2);
			job3.SetPathGroup(2, 1, 1);
			job3.SetPathGroup(2, 2, 2);

			//These do not all match above (some do, but not all)
			job3.AddProcessOnPallet(1, 1, "4");
			job3.AddProcessOnPallet(1, 1, "5");
			job3.AddProcessOnPallet(1, 2, "22");
			job3.AddProcessOnPallet(1, 2, "23");
			job3.AddProcessOnPallet(1, 2, "24");
			job3.AddProcessOnPallet(2, 1, "30");
			job3.AddProcessOnPallet(2, 1, "31");
			job3.AddProcessOnPallet(2, 2, "100");
			job3.AddProcessOnPallet(2, 2, "110");
			job3.AddProcessOnPallet(2, 2, "120");

			AddBasicStopsWithProg(job3);

			var log = new List<string>();
			var trace = new List<string>();

			var dset = new ReadOnlyDataSet();
			CreateProgram(dset, "1234");

		    var pMap = new clsPalletPartMapping(new JobPlan[] {job1, job2, job3}, dset, 3,
			                                    new List<string>(), log, trace, true, "NewGlobal",
			                                    false,  DatabaseAccess.MazakDbType.MazakVersionE);

			//Console.WriteLine(DatabaseAccess.Join(trace, Environment.NewLine));
			if (log.Count > 0) Assert.True(false, log[0]);

			CheckNewFixtures(pMap, new string[] {
				"Fixt:3:0:4:1",
				"Fixt:3:1:40:2",
				"Fixt:3:2:10:1",
				"Fixt:3:3:100:2",
				"Fixt:3:4:30:2",
				"Fixt:3:5:22:1"
			});

			var trans = new TransactionDataSet();
			pMap.CreateRows(trans);

			CheckPartProcessFromJob(trans, "Part1:3:1", 1, "Fixt:3:0:4:1");
			CheckPartProcessFromJob(trans, "Part1:3:1", 2, "Fixt:3:1:40:2");
			CheckPart(trans, "Part1:3:1", "Job1-Path1-0");

			CheckPartProcessFromJob(trans, "Part1:3:2", 1, "Fixt:3:2:10:1");
			CheckPartProcessFromJob(trans, "Part1:3:2", 2, "Fixt:3:3:100:2");
			CheckPart(trans, "Part1:3:2", "Job1-Path2-0");

			CheckPartProcessFromJob(trans, "Part2:3:1", 1, "Fixt:3:0:4:1");
			CheckPartProcessFromJob(trans, "Part2:3:1", 2, "Fixt:3:1:40:2");
			CheckPart(trans, "Part2:3:1", "Job2-Path1-0");

			CheckPartProcessFromJob(trans, "Part2:3:2", 1, "Fixt:3:2:10:1");
			CheckPartProcessFromJob(trans, "Part2:3:2", 2, "Fixt:3:3:100:2");
			CheckPart(trans, "Part2:3:2", "Job2-Path2-0");

			CheckPartProcessFromJob(trans, "Part3:3:1", 1, "Fixt:3:0:4:1");
			CheckPartProcessFromJob(trans, "Part3:3:1", 2, "Fixt:3:4:30:2");
			CheckPart(trans, "Part3:3:1", "Job3-Path1-0");

			CheckPartProcessFromJob(trans, "Part3:3:2", 1, "Fixt:3:5:22:1");
			CheckPartProcessFromJob(trans, "Part3:3:2", 2, "Fixt:3:3:100:2");
			CheckPart(trans, "Part3:3:2", "Job3-Path2-0");

			CheckSingleProcPalletGroup(trans, 31, "Fixt:3:0:4:1", new int[] {4, 5});
			CheckSingleProcPalletGroup(trans, 32, "Fixt:3:1:40:2", new int[] {40, 50});
			CheckSingleProcPalletGroup(trans, 33, "Fixt:3:2:10:1", new int[] {10, 11, 12});
			CheckSingleProcPalletGroup(trans, 34, "Fixt:3:3:100:2", new int[] {100, 110, 120});
			CheckSingleProcPalletGroup(trans, 35, "Fixt:3:4:30:2", new int[] {30, 31});
			CheckSingleProcPalletGroup(trans, 36, "Fixt:3:5:22:1", new int[] {22, 23, 24});

			AssertPartsPalletsDeleted(trans);
		}

		#region Checking
		private ReadOnlyDataSet CreateReadSet()
		{
			var dset = new ReadOnlyDataSet();
			CreateFixture(dset, "Test");
			return dset;
		}

		private void CreatePart(ReadOnlyDataSet dset, string unique, string name, int numProc, string fix)
		{
			var pRow = dset.Part.AddPartRow("comment", 0, name, 0, 0);

			for (int proc = 1; proc <= numProc; proc++) {
				dset.PartProcess.AddPartProcessRow(0, "", 0, "", "", 1, fix + ":" + proc.ToString(), "2", pRow, proc, "", "", 0, 0);
			}
		}

		private void CreateFixture(ReadOnlyDataSet dset, string name)
		{
			dset.Fixture.AddFixtureRow("comment", name, 0, 0);
		}

		private void CreatePallet(ReadOnlyDataSet dset, int pal, string fix, int numProc)
		{
			for (int i = 1; i <= numProc; i++) {
				dset.Pallet.AddPalletRow(999, fix + ":" + i.ToString(), pal, 0, 0, 0);
			}
		}

		private void CreateProgram(ReadOnlyDataSet dset, string program)
		{
			dset.MainProgram.AddMainProgramRow(program, "", 0);
		}

		private void AddBasicStopsWithProg(JobPlan job)
		{
			for (int proc = 1; proc <= job.NumProcesses; proc++) {
				for (int path = 1; path <= job.GetNumPaths(proc); path++) {
					job.AddLoadStation(proc, path, 1);
					job.AddUnloadStation(proc, path, 1);
					var stop = new JobMachiningStop("machine");
					stop.AddProgram(1, "1234");
					job.AddMachiningStop(proc, path, stop);
				}
			}
		}

		private void CheckNewFixtures(clsPalletPartMapping map, ICollection<string> newFix)
		{
			var trans = new TransactionDataSet();
			map.AddFixtures(trans);

			foreach (string fix in newFix) {
				foreach (TransactionDataSet.Fixture_tRow row in new System.Collections.ArrayList(trans.Fixture_t.Rows)) {
					if (row.FixtureName == fix) {
						row.Delete();
						goto found;
					}
				}
				Assert.True(false, "Did not create fixture " + fix);
			found:;
			}

			foreach (TransactionDataSet.Fixture_tRow row in trans.Fixture_t) {
				if (row.RowState != System.Data.DataRowState.Deleted)
					Assert.True(false, "Extra fixture created: " + row.FixtureName);
			}
		}

		private void CheckPartProcess(TransactionDataSet dset, string part, int proc, string fixture)
		{
			CheckPartProcess(dset, part, proc, fixture, "0000000000", "0000000000", "00000000");
		}
		private void CheckPartProcessFromJob(TransactionDataSet dset, string part, int proc, string fixture)
		{
			//checks stuff created with AddBasicStopsWithProg
			CheckPartProcess(dset, part, proc, fixture, "1000000000", "1000000000", "10000000");
		}

		private void CheckPartProcess(TransactionDataSet dset, string part, int proc, string fixture,
		                              string fix, string rem, string cut)
		{
			foreach (TransactionDataSet.PartProcess_tRow row in dset.PartProcess_t.Rows) {
				if (row.RowState != System.Data.DataRowState.Deleted &&
				    row.PartName == part && row.ProcessNumber == proc) {
					row.Fixture.Should().Be(fixture, because: "on " + part);
					row.FixLDS.Should().Be(fix, because: "on " + part);
					row.RemoveLDS.Should().Be(rem, because: "on " + part);
					row.CutMc.Should().Be(cut, because: "on " + part);
					row.Delete();
					break;
				}
			}
		}

		private void CheckPart(TransactionDataSet dset, string part, string comment)
		{
			foreach (TransactionDataSet.Part_tRow row in dset.Part_t.Rows) {
				if (row.RowState != System.Data.DataRowState.Deleted &&
				    row.PartName == part) {
					Assert.Equal(comment, row.Comment);
					row.Delete();
					break;
				}
			}
		}

		private void CheckSingleProcPalletGroup(TransactionDataSet dset, int groupNum, string fix, IList<int> pals)
		{
			int angle = groupNum * 1000;

			foreach (int pal in pals) {
				int angle2 = CheckPalletV1(dset, fix, pal);
				if (angle2 == -1)
					Assert.True(false, "Unable to find pallet " + pal.ToString() + " " + fix);

				angle2.Should().Be(angle, because: "in same pallet group " + fix + " " + pal);

				int g = CheckPalletV2(dset, fix, pal);
				if (g == -1)
					Assert.True(false, "Unable to find pallet " + pal.ToString() + " " + fix);

				g.Should().Be(groupNum, because: "in same pallet group " + fix + " " + pal);
			}
		}

		private void CheckPalletGroup(TransactionDataSet dset, int groupNum, string fix, int numProc, IList<int> pals)
		{
			int angle = groupNum * 1000;

			foreach (int pal in pals) {
				for (int i = 1; i <= numProc; i++) {
					int angle2 = CheckPalletV1(dset, fix + ":" + i.ToString(), pal);
					if (angle2 == -1)
						Assert.True(false, "Unable to find pallet " + pal.ToString() + " " + fix);

					angle2.Should().Be(angle, because: "in same pallet group " + fix + " " + pal);

					int g = CheckPalletV2(dset, fix + ":" + i.ToString(), pal);
					if (g == -1)
						Assert.True(false, "Unable to find pallet " + pal.ToString() + " " + fix);

					g.Should().Be(groupNum, because: "in same pallet group " + fix + " " + pal);
				}
			}
		}

		private int CheckPalletV1(TransactionDataSet dset, string fix, int pal)
		{
			foreach (TransactionDataSet.Pallet_tV1Row row in dset.Pallet_tV1.Rows) {
				if (row.RowState != System.Data.DataRowState.Deleted &&
				    row.PalletNumber == pal && row.Fixture == fix) {
					int angle = row.Angle;
					row.Delete();
					return angle;
				}
			}

			return -1;
		}

		private int CheckPalletV2(TransactionDataSet dset, string fix, int pal)
		{
			foreach (TransactionDataSet.Pallet_tV2Row row in dset.Pallet_tV2.Rows) {
				if (row.RowState != System.Data.DataRowState.Deleted &&
				    row.PalletNumber == pal && row.Fixture == fix) {
					int g = row.FixtureGroup;
					row.Delete();
					return g;
				}
			}
			return -1;
		}

		private void AssertPartsPalletsDeleted(TransactionDataSet dset)
		{
			foreach (TransactionDataSet.PartProcess_tRow row in dset.PartProcess_t.Rows) {
				if (row.RowState != System.Data.DataRowState.Deleted)
					Assert.True(false, "Extra part process row: " + row.PartName + " " + row.ProcessNumber.ToString());
			}

			foreach (TransactionDataSet.Part_tRow row in dset.Part_t.Rows) {
				if (row.RowState != System.Data.DataRowState.Deleted)
					Assert.True(false, "Extra part row: " + row.PartName);
			}

			foreach (TransactionDataSet.Pallet_tV1Row row in dset.Pallet_tV1.Rows) {
				if (row.RowState != System.Data.DataRowState.Deleted)
					Assert.True(false, "Extra pallet row: " + row.PalletNumber.ToString() + " " + row.Fixture);
			}

			foreach (TransactionDataSet.Pallet_tV2Row row in dset.Pallet_tV2.Rows) {
				if (row.RowState != System.Data.DataRowState.Deleted)
					Assert.True(false, "Extra pallet v2 row: " + row.PalletNumber.ToString() + " " + row.Fixture);
			}
		}
		#endregion
	}
}